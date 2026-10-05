using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>Stores a valid upload and submits a processing job. Metadata and estimator inputs are
/// resolved from the file or explicit defaults when processing starts, independently of the fleet.</summary>
public sealed class AddPartCommandHandler(
    IPartFileClassifier classifier,
    IPartFileStorage storage,
    IPartProcessingJobRepository jobRepository,
    IPartProcessingDispatcher queue,
    IOrganizationRepository organizationRepository,
    ResolveSlicerSelection resolveSlicerSelection) : ICommandHandler<AddPartCommand, AddPartResult>
{
    public async Task<AddPartResult> HandleAsync(AddPartCommand command, CancellationToken cancellationToken)
    {
        var classification = classifier.Classify(command.File, command.FileName);
        if (command.File.CanSeek)
        {
            command.File.Position = 0;
        }

        // research.md R6 — hashed here, in the Application-layer caller, before storage keys the
        // path on it (content-addressed storage, FR-009d).
        var contentHash = await PartFileHasher.ComputeHashAsync(command.File, cancellationToken);
        if (command.File.CanSeek)
        {
            command.File.Position = 0;
        }

        decimal? targetNozzleDiameterMm = null;
        SlicerRef? slicerUsed = null;
        string? targetMaterialType = null;

        if (classification.RequiresSlicing)
        {
            var organization = await organizationRepository.GetAsync(cancellationToken);

            // Keep the chosen engine/version reproducible even if estimation is unavailable.
            try
            {
                slicerUsed = resolveSlicerSelection.Resolve(command.SlicerEngineOverride, command.SlicerVersionOverride, organization.DefaultSlicer);
            }
            catch (SlicerUnavailableException)
            {
                // Preserve an explicit pair for reproducible retries even when it is unavailable.
                if (command.SlicerEngineOverride is { } engine && command.SlicerVersionOverride is { } version)
                    slicerUsed = new SlicerRef(engine, version);
            }


        }

        var job = PartProcessingJob.CreateAdd(
            Organization.WellKnownId,
            classification.Format,
            command.FileName,
            storagePath: string.Empty,
            fileSizeBytes: 0,
            classification.RequiresSlicing,
            slicerUsed?.Engine,
            slicerUsed?.Version,
            targetNozzleDiameterMm,
            targetMaterialType,
            command.RequestedByUserId);

        // The job's own id becomes the storage key — an Add's target Part doesn't exist yet
        // (IPartFileStorage's remarks). Create* factories don't take a pre-generated id, so the
        // storage path/size is set post-construction, once the id is actually known.
        var storagePath = await storage.SaveAsync(Organization.WellKnownId, contentHash, command.FileName, command.File, cancellationToken);
        var fileSizeBytes = command.File.CanSeek ? command.File.Length : 0;
        job.SetStoragePath(storagePath, fileSizeBytes);

        await jobRepository.AddAsync(job, cancellationToken);
        queue.Enqueue(new PartProcessingWorkItem(Organization.WellKnownId, job.Id));

        return new AddPartResult(job.Id, job.Status);
    }
}
