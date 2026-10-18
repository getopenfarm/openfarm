using OpenFarm.Application.Parts;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class ProductionSnapshotFactory(ResolveSlicerSelection slicers, IFarmSettingsRepository farmSettings)
{
    public async Task CaptureAsync(PrintJob job, Part part, CancellationToken ct)
    {
        SlicerRef? slicer = null;
        if (part.RequiresSlicing)
            slicer = slicers.Resolve(part.SlicerEngineOverride, part.SlicerVersionUsed,
                (await farmSettings.GetAsync(ct)).DefaultSlicer);
        job.CaptureProductionSnapshot(part, slicer);
    }
}
