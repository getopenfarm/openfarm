using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Test-after (research.md §12) — pure dispatch/orchestration, not the parsing/slicing logic itself.
// The single seam PartProcessingBackgroundService depends on (research.md §2): unifies the parse
// (already-sliced) and slice (requires-slicing) paths behind IPartFileProcessor.
public sealed class PartFileProcessor(
    IAlreadySlicedFileMetadataParser slicedMetadataParser,
    IUnslicedProjectMaterialResolver unslicedProjectMaterialResolver,
    ISlicerEngine slicerEngine) : IPartFileProcessor
{
    public async Task<PartMaterialResolution> ResolveMaterialAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken)
    {
        if (!job.RequiresSlicing)
        {
            var metadata = await slicedMetadataParser.ParseAsync(fileStream, job.FileFormat, cancellationToken);
            var requirements = FilamentRequirementGrouper.Group(metadata.DetectedFilamentSlots);

            return new PartMaterialResolution(
                metadata.NozzleDiameterMm,
                requirements,
                metadata.PlateCount,
                new PartEstimateResolution(metadata.EstimatedPrintTimeMinutes, metadata.EstimatedFilamentWeightGrams, metadata.Thumbnails));
        }

        var nozzleDiameterMm = job.TargetNozzleDiameterMm
            ?? throw new NoNozzleDiameterAvailableException();

        if (job.FileFormat == PartFileFormat.Stl)
        {
            // An STL structurally carries no material data — the sole "any material" wildcard case
            // (research.md §7a). job.TargetMaterialType already holds the concrete fixed fallback
            // profile actually used to slice; the requirement itself stays unconstrained (null).
            return new PartMaterialResolution(
                nozzleDiameterMm,
                [new PreviewFilamentRequirement(0, MaterialType: null, DetectedColorHex: null, DetectedSubBrand: null)],
                PlateCount: 1,
                ImmediateEstimate: null,
                ResolvedTargetMaterialType: job.TargetMaterialType);
        }

        // Unsliced 3MF (research.md §13) — material resolves from the project's own filament-preset
        // assignment, never the operator.
        var projectMaterial = await unslicedProjectMaterialResolver.ResolveAsync(fileStream, cancellationToken);
        return new PartMaterialResolution(
            nozzleDiameterMm,
            [new PreviewFilamentRequirement(0, projectMaterial.MaterialType, DetectedColorHex: null, DetectedSubBrand: null)],
            projectMaterial.PlateCount,
            ImmediateEstimate: null,
            ResolvedTargetMaterialType: projectMaterial.MaterialType);
    }

    public async Task<PartEstimateResolution> ResolveEstimateAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken)
    {
        var engine = job.SlicerEngineUsed ?? throw new SlicingFailedException("No slicer engine was resolved for this job.");
        var version = job.SlicerVersionUsed ?? throw new SlicingFailedException("No slicer version was resolved for this job.");
        var materialType = job.TargetMaterialType ?? throw new SlicingFailedException("No target material was resolved for this job.");
        var nozzleDiameterMm = job.TargetNozzleDiameterMm ?? throw new SlicingFailedException("No target nozzle diameter was resolved for this job.");

        // No specific target printer at pre-print estimate time (research.md §5's estimate-only,
        // fleet-representative case, FR-015) — this is the pipeline's only slicing call site.
        var result = await slicerEngine.SliceAsync(
            new SlicerRef(engine, version), fileStream, job.FileFormat, materialType, nozzleDiameterMm, printerId: null, cancellationToken);
        return new PartEstimateResolution(result.EstimatedPrintTimeMinutes, result.EstimatedFilamentWeightGrams, result.Thumbnails);
    }
}
