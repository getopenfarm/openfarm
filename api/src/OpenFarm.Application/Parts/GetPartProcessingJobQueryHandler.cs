using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class GetPartProcessingJobQueryHandler(IPartProcessingJobRepository jobRepository)
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
        var preview = showPreview
            ? new PartProcessingJobPreviewDetail(
                job.PreviewNozzleDiameterMm,
                job.PreviewFilamentRequirements,
                job.PreviewEstimateStatus!.Value.ToString(),
                job.PreviewEstimatedPrintTimeMinutes,
                job.PreviewEstimatedFilamentWeightGrams,
                job.PreviewPlateCount,
                job.PreviewThumbnails.Select(t => new ThumbnailView(t.PlateIndex, Convert.ToBase64String(t.Bytes))).ToList())
            : null;

        var slicer = job.SlicerEngineUsed is { } engine && job.SlicerVersionUsed is { } version
            ? new SlicerSelectionView(engine.ToString(), version)
            : null;

        return new PartProcessingJobDetail(job.Id, job.Kind.ToString(), job.Status.ToString(), job.PartId, slicer, preview, job.ErrorMessage);
    }
}
