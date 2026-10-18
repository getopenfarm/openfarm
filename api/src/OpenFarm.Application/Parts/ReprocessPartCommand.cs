using OpenFarm.Application.Common;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record ReprocessPartCommand(Guid PartId, Guid? RequestedByUserId);

public sealed class ReprocessPartCommandHandler(
    IPartRepository parts, IPartProcessingJobRepository jobs, IPartProcessingDispatcher dispatcher,
    IFarmSettingsRepository farmSettings, ResolveSlicerSelection selection)
    : ICommandHandler<ReprocessPartCommand, AddPartResult>
{
    public async Task<AddPartResult> HandleAsync(ReprocessPartCommand command, CancellationToken cancellationToken)
    {
        var part = await parts.GetByIdAsync(command.PartId, cancellationToken) ?? throw new PartNotFoundException(command.PartId);
        if (await jobs.HasActiveReprocessAsync(part.Id, cancellationToken)) throw new PartConcurrencyConflictException(part.Id);
        SlicerRef? slicer = null;
        decimal? nozzle = null;
        string? material = null;
        if (part.RequiresSlicing)
        {
            var settings = await farmSettings.GetAsync(cancellationToken);
            try
            {
                slicer = selection.Resolve(part.SlicerEngineOverride, part.SlicerVersionUsed, settings.DefaultSlicer);
            }
            catch (SlicerUnavailableException)
            {
                if (part.SlicerEngineOverride is { } engine && part.SlicerVersionUsed is { } version)
                    slicer = new SlicerRef(engine, version);
            }
        }
        var job = PartProcessingJob.CreateReprocess(part, slicer, nozzle, material, command.RequestedByUserId);
        await jobs.AddAsync(job, cancellationToken);
        dispatcher.Enqueue(new PartProcessingWorkItem(job.Id));
        return new AddPartResult(job.Id, job.Status);
    }
}
