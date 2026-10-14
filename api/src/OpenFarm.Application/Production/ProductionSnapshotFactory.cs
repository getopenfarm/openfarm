using OpenFarm.Application.Organizations;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class ProductionSnapshotFactory(ResolveSlicerSelection slicers, IOrganizationRepository organizations)
{
    public async Task CaptureAsync(PrintJob job, Part part, CancellationToken ct)
    {
        SlicerRef? slicer = null;
        if (part.RequiresSlicing)
            slicer = slicers.Resolve(part.SlicerEngineOverride, part.SlicerVersionUsed,
                (await organizations.GetAsync(ct)).DefaultSlicer);
        job.CaptureProductionSnapshot(part, slicer);
    }
}
