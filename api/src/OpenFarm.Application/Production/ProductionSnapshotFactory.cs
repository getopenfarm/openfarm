using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class ProductionSnapshotFactory(ResolveSlicerSelection slicers)
{
    public Task CaptureAsync(PrintJob job, Part part, CancellationToken ct)
    {
        // The job keeps the part's own pin: a later change of the farm default never moves a queued job to another slicer.
        job.CaptureProductionSnapshot(part, part.RequiresSlicing ? slicers.ResolvePinned(part.SlicerEngineOverride, part.SlicerVersionUsed) : null);
        return Task.CompletedTask;
    }
}
