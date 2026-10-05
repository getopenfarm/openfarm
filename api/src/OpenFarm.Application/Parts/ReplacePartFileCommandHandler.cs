using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>Same shape as <see cref="AddPartCommandHandler"/>, targeting an existing Part — the Part itself is untouched until Confirm (data-model.md's Submit step, FR-004).</summary>
public sealed class ReplacePartFileCommandHandler(
    IPartFileClassifier classifier,
    IPartFileStorage storage,
    IPartRepository partRepository,
    IPartProcessingJobRepository jobRepository,
    IPartProcessingDispatcher queue,
    IOrganizationRepository organizationRepository,
    ResolveSlicerSelection resolveSlicerSelection) : ICommandHandler<ReplacePartFileCommand, AddPartResult>
{
    public async Task<AddPartResult> HandleAsync(ReplacePartFileCommand command, CancellationToken cancellationToken)
    {
        _ = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

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

        var job = PartProcessingJob.CreateReplace(
            Organization.WellKnownId,
            command.PartId,
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

        var storagePath = await storage.SaveAsync(Organization.WellKnownId, contentHash, command.FileName, command.File, cancellationToken);
        var fileSizeBytes = command.File.CanSeek ? command.File.Length : 0;
        job.SetStoragePath(storagePath, fileSizeBytes);

        await jobRepository.AddAsync(job, cancellationToken);
        queue.Enqueue(new PartProcessingWorkItem(Organization.WellKnownId, job.Id));

        return new AddPartResult(job.Id, job.Status);
    }
}
