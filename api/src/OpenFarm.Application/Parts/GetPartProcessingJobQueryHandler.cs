using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class GetPartProcessingJobQueryHandler(IPartProcessingJobRepository jobRepository, IPartRepository? partRepository = null)
    : IQueryHandler<GetPartProcessingJobQuery, PartProcessingJobDetail?>
{
    public async Task<PartProcessingJobDetail?> HandleAsync(GetPartProcessingJobQuery query, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(query.JobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        // Preview reflects what was derived (ReadyForReview) or what was derived as of confirm time
        // (Confirmed) — null for every other status (contracts/part-library-api.md).
        var showPreview = job.Status is PartProcessingJobStatus.ReadyForReview or PartProcessingJobStatus.Confirmed;
        var warnings = job.ProcessingWarnings.ToList();
        if (showPreview && job.Kind == PartProcessingJobKind.Reprocess && partRepository is not null && job.PartId is { } partId)
        {
            var source = await partRepository.GetByIdAsync(partId, cancellationToken);
            if (source?.FilamentRequirements.Any(r => (r.NarrowFilamentProfileId is not null || r.NarrowFilamentVariantId is not null)
                && !job.PreviewFilamentRequirements.Any(p => p.MaterialType == r.MaterialType && p.DetectedColorHex == r.DetectedColorHex && p.DetectedSubBrand == r.DetectedSubBrand)) == true)
                warnings.Add("Some previous filament selections no longer match the extracted requirements and will be cleared when you save.");
        }
        var preview = showPreview
            ? new PartProcessingJobPreviewDetail(
                job.PreviewNozzleDiameterMm,
                job.PreviewFilamentRequirements,
                job.PreviewEstimateStatus!.Value.ToString(),
                job.PreviewEstimatedPrintTimeMinutes,
                job.PreviewEstimatedFilamentWeightGrams,
                job.PreviewPlates.Select(p => new PlateView(p.Number, p.Name, p.EstimatedPrintTimeMinutes, p.EstimatedFilamentWeightGrams,
                    job.PlateRequirements(p))).ToList(),
                job.PreviewThumbnails.Select(t => new ThumbnailView(t.PlateIndex, Convert.ToBase64String(t.Bytes))).ToList(),
                job.PrintRequirementsStatus.ToString(), warnings, job.EstimateUnavailableReason, job.Estimation)
            : null;

        var slicer = job.SlicerEngineUsed is { } engine && job.SlicerVersionUsed is { } version
            ? new SlicerSelectionView(engine.ToString(), version)
            : null;

        return new PartProcessingJobDetail(job.Id, job.Kind.ToString(), job.Status.ToString(), job.PartId, slicer, preview, job.ErrorMessage);
    }
}
