using OpenFarm.Application.Organizations;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// The two FR-011 branches: an already-sliced <see cref="Part"/> (<c>RequiresSlicing == false</c>) is
/// read back from <see cref="IPartFileStorage"/> as-is; a part requiring slicing is re-sliced on
/// demand via <see cref="ISlicerEngine"/>, mirroring the exact slicer-selection call shape
/// <c>AddPartCommandHandler</c>/<c>ReplacePartFileCommandHandler</c> already use (Constitution III —
/// one centralized resolution rule) so a job prints with the same engine/version its part was
/// confirmed with. The re-slice result is read into memory and never written back to
/// <see cref="Part.StoragePath"/> — scoped to this one dispatch only, per FR-011.
/// </summary>
public sealed class JobFileResolver(
    IPartFileStorage partFileStorage,
    ISlicerEngine slicerEngine,
    ResolveSlicerSelection resolveSlicerSelection,
    ISlicerDefaultsProvider slicerDefaults,
    IOrganizationRepository organizationRepository,
    IPartFileClassifier classifier) : IJobFileResolver
{
    public async Task<JobFileContent> ResolveAsync(Part part, PrintJob job, Guid printerId, decimal nozzleDiameterMm, CancellationToken cancellationToken)
    {
        var storagePath = job.FileRevisionHash == part.RevisionHash
            ? part.StoragePath
            : await partFileStorage.GetRevisionStoragePathAsync(part.OrganizationId, job.FileRevisionHash, part.OriginalFileName, cancellationToken);
        await using var stored = await partFileStorage.OpenReadAsync(storagePath, cancellationToken);
        var fileName = Path.GetFileName(storagePath);
        var classification = classifier.Classify(stored, fileName);
        stored.Position = 0;
        if (!classification.RequiresSlicing)
        {
            using var storedBuffer = new MemoryStream();
            await stored.CopyToAsync(storedBuffer, cancellationToken);
            return new JobFileContent(storedBuffer.ToArray(), fileName);
        }

        if (printerId == Guid.Empty) throw new JobNotEligibleForRemoteControlException(job.Id);
        var organization = await organizationRepository.GetAsync(cancellationToken);
        var slicerRef = resolveSlicerSelection.Resolve(part.SlicerEngineOverride, part.SlicerVersionUsed, organization.DefaultSlicer);
        var materialType = (job.FilamentConsumption.Count > 0 ? job.FilamentConsumption[0].MaterialType : null)
            ?? slicerDefaults.GetFallbackMaterialType();

        var result = await slicerEngine.SliceAsync(slicerRef, stored, classification.Format, materialType, nozzleDiameterMm, printerId, cancellationToken);

        return new JobFileContent(result.OutputFileBytes, $"{part.Name}.gcode");
    }
}
