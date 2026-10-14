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
    IOrganizationRepository organizationRepository,
    IPartFileClassifier classifier, ProductionProfileReadiness readiness,
    ResolveSlicingProfileSelection profileSelection, OpenFarm.Application.Printers.IPrinterRepository printers,
    ProductionJobRequirements jobRequirements) : IJobFileResolver
{
    public async Task<JobFileContent> ResolveAsync(Part part, PrintJob job, Guid printerId, decimal nozzleDiameterMm, CancellationToken cancellationToken)
    {
        part = job.GetProductionPart() ?? throw new ProductionSnapshotUnavailableException(job.Id, job.SnapshotRecoveryReason);
        part.EnsurePrintRequirementsComplete();
        if (printerId == Guid.Empty) throw new SlicingProfileUnconfiguredException("Assign a printer before generating a production file.");
        var printer = await printers.GetByIdAsync(printerId, cancellationToken)
            ?? throw new OpenFarm.Application.Printers.PrinterNotFoundException(printerId);
        if (!printer.NozzleConfirmed || printer.ReportedNozzleDiameterMm != nozzleDiameterMm)
            throw new SlicingProfileUnconfiguredException("The printer nozzle must be confirmed by current telemetry before production.");
        var effective = await jobRequirements.ResolveAsync(job, part, cancellationToken);
        var selections = await readiness.ResolveAsync(printerId, part,
            effective.Requirements, cancellationToken, effective.RevisionHash);
        var storagePath = effective.RevisionHash == part.RevisionHash
            ? part.StoragePath
            : await partFileStorage.GetRevisionStoragePathAsync(part.OrganizationId, effective.RevisionHash, part.OriginalFileName, cancellationToken);
        await using var stored = await partFileStorage.OpenReadAsync(storagePath, cancellationToken);
        var fileName = Path.GetFileName(storagePath);
        var classification = classifier.Classify(stored, fileName);
        stored.Position = 0;
        if (!classification.RequiresSlicing)
        {
            using var storedBuffer = new MemoryStream();
            await stored.CopyToAsync(storedBuffer, cancellationToken);
            var description = PrintFileName.Describe(part.Name, selections.Select(s => s.MaterialType),
                effective.RevisionHash == part.RevisionHash ? part.EstimatedPrintTimeMinutes : null);
            var storedExtension = classification.Format == PartFileFormat.ThreeMf ? ".gcode.3mf" : ".gcode";
            return new JobFileContent(storedBuffer.ToArray(), $"{description}{storedExtension}");
        }

        if (printerId == Guid.Empty) throw new JobNotEligibleForRemoteControlException(job.Id);
        var organization = await organizationRepository.GetAsync(cancellationToken);
        var slicerRef = resolveSlicerSelection.Resolve(part.SlicerEngineOverride, part.SlicerVersionUsed, organization.DefaultSlicer);
        var selection = selections[0];
        var machineId = printer.MachineProfileId ?? throw new SlicingProfileUnconfiguredException("Select a machine profile.");
        var processId = printer.ProcessProfileId ?? throw new SlicingProfileUnconfiguredException("Select a process profile.");
        var plate = printer.BuildPlateType ?? throw new SlicingProfileUnconfiguredException("Select a build plate.");
        var profiles = await profileSelection.ResolveProfilesAsync(machineId, processId, selection.Profile.Id, plate, cancellationToken, slicerRef.Engine);
        var result = await slicerEngine.SliceConfiguredAsync(slicerRef, stored, classification.Format, selection.MaterialType,
            nozzleDiameterMm, printerId, profiles, cancellationToken);

        var extension = result.OutputFileFormat == PartFileFormat.ThreeMf ? ".gcode.3mf" : ".gcode";
        var slicedDescription = PrintFileName.Describe(part.Name, selections.Select(s => s.MaterialType), result.EstimatedPrintTimeMinutes);
        return new JobFileContent(result.OutputFileBytes, $"{slicedDescription}{extension}");
    }
}
