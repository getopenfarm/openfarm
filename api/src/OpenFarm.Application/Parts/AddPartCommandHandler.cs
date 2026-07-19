using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// Classifies synchronously, resolves a requires-slicing file's nozzle diameter/engine/fallback
/// material up front (data-model.md's Submit step — <c>400</c>/<c>422</c> here, no job created if
/// either fails), persists the file, creates the job, and enqueues background processing. Never
/// creates a <see cref="Part"/> itself (research.md §2).
/// </summary>
public sealed class AddPartCommandHandler(
    IPartFileClassifier classifier,
    IPartFileStorage storage,
    IPartProcessingJobRepository jobRepository,
    IPartProcessingQueue queue,
    IPrinterRepository printerRepository,
    IMachineProfileRepository machineProfileRepository,
    IProfileBundleParser profileBundleParser,
    IOrganizationRepository organizationRepository,
    ISlicerDefaultsProvider slicerDefaults) : ICommandHandler<AddPartCommand, AddPartResult>
{
    public async Task<AddPartResult> HandleAsync(AddPartCommand command, CancellationToken cancellationToken)
    {
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

            // An STL's fallback material is known synchronously (research.md §7a); an unsliced 3MF's
            // real target is only discovered during background milestone-1 processing (research.md §13).
            if (classification.Format == PartFileFormat.Stl)
            {
                targetMaterialType = slicerDefaults.GetGenericFallbackMaterialProfile(slicerEngineUsed.Value);
            }
        }

        var job = PartProcessingJob.CreateAdd(
            Organization.WellKnownId,
            classification.Format,
            command.FileName,
            storagePath: string.Empty,
            fileSizeBytes: 0,
            classification.RequiresSlicing,
            slicerEngineUsed,
            targetNozzleDiameterMm,
            targetMaterialType,
            command.RequestedByUserId);

        // The job's own id becomes the storage key — an Add's target Part doesn't exist yet
        // (IPartFileStorage's remarks). Create* factories don't take a pre-generated id, so the
        // storage path/size is set post-construction, once the id is actually known.
        var storagePath = await storage.SaveAsync(Organization.WellKnownId, job.Id, command.FileName, command.File, cancellationToken);
        var fileSizeBytes = command.File.CanSeek ? command.File.Length : 0;
        job.SetStoragePath(storagePath, fileSizeBytes);

        await jobRepository.AddAsync(job, cancellationToken);
        queue.Enqueue(new PartProcessingWorkItem(Organization.WellKnownId, job.Id));

        return new AddPartResult(job.Id, job.Status);
    }
}
