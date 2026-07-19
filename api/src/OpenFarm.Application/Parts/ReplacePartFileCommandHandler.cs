using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>Same shape as <see cref="AddPartCommandHandler"/>, targeting an existing Part — the Part itself is untouched until Confirm (data-model.md's Submit step, FR-004).</summary>
public sealed class ReplacePartFileCommandHandler(
    IPartFileClassifier classifier,
    IPartFileStorage storage,
    IPartRepository partRepository,
    IPartProcessingJobRepository jobRepository,
    IPartProcessingQueue queue,
    IPrinterRepository printerRepository,
    IMachineProfileRepository machineProfileRepository,
    IProfileBundleParser profileBundleParser,
    IOrganizationRepository organizationRepository,
    ISlicerDefaultsProvider slicerDefaults) : ICommandHandler<ReplacePartFileCommand, AddPartResult>
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

        decimal? targetNozzleDiameterMm = null;
        SlicerEngine? slicerEngineUsed = null;
        string? targetMaterialType = null;

        if (classification.RequiresSlicing)
        {
            var printers = await printerRepository.ListAsync(cancellationToken);
            var profiles = await machineProfileRepository.ListAsync(cancellationToken);
            targetNozzleDiameterMm = NozzleDiameterResolver.Resolve(printers, profiles, profileBundleParser)
                ?? throw new NoNozzleDiameterAvailableException();

            var organization = await organizationRepository.GetAsync(cancellationToken);
            slicerEngineUsed = command.SlicerEngineOverride ?? organization.DefaultSlicerEngine;

            if (classification.Format == PartFileFormat.Stl)
            {
                targetMaterialType = slicerDefaults.GetGenericFallbackMaterialProfile(slicerEngineUsed.Value);
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
            slicerEngineUsed,
            targetNozzleDiameterMm,
            targetMaterialType,
            command.RequestedByUserId);

        var storagePath = await storage.SaveAsync(Organization.WellKnownId, job.Id, command.FileName, command.File, cancellationToken);
        var fileSizeBytes = command.File.CanSeek ? command.File.Length : 0;
        job.SetStoragePath(storagePath, fileSizeBytes);

        await jobRepository.AddAsync(job, cancellationToken);
        queue.Enqueue(new PartProcessingWorkItem(Organization.WellKnownId, job.Id));

        return new AddPartResult(job.Id, job.Status);
    }
}
