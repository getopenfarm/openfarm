using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubPartEventPublisher(IHubContext<FarmHub> hubContext) : IPartEventPublisher
{
    public Task PartProcessingJobStatusChangedAsync(PartProcessingJob job, CancellationToken cancellationToken) =>
        Group.SendAsync(
            "PartProcessingJobStatusChanged",
            new
            {
                jobId = job.Id,
                status = job.Status.ToString(),
                partId = job.PartId,
                errorMessage = job.ErrorMessage,
                preview = job.Status == PartProcessingJobStatus.ReadyForReview ? BuildPreview(job) : null,
            },
            cancellationToken);

    public Task PartUpsertedAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartUpserted", new { partId }, cancellationToken);

    public Task PartEstimateResolvedAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartEstimateResolved", new { partId }, cancellationToken);

    public Task PartEstimateUnavailableAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartEstimateUnavailable", new { partId }, cancellationToken);

    public Task PartDeletedAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartDeleted", new { partId }, cancellationToken);

    private static object BuildPreview(PartProcessingJob job) => new
    {
        nozzleDiameterMm = job.PreviewNozzleDiameterMm,
        filamentRequirements = job.PreviewFilamentRequirements.Select(r => new
        {
            requirementIndex = r.RequirementIndex,
            materialType = r.MaterialType,
            detectedColorHex = r.DetectedColorHex,
            detectedSubBrand = r.DetectedSubBrand,
            narrowFilamentVariantId = r.NarrowFilamentVariantId,
            narrowFilamentProfileId = r.NarrowFilamentProfileId,
            estimatedGrams = r.EstimatedGrams,
        }),
        estimateStatus = job.PreviewEstimateStatus.ToString(),
        estimatedPrintTimeMinutes = job.PreviewEstimatedPrintTimeMinutes,
        estimatedFilamentWeightGrams = job.PreviewEstimatedFilamentWeightGrams,
        plateCount = job.PreviewPlateCount,
        thumbnails = job.PreviewThumbnails.Select(t => new
        {
            plateIndex = t.PlateIndex,
            thumbnailBase64 = Convert.ToBase64String(t.Bytes),
        }),
    };

    private IClientProxy Group => hubContext.Clients.Group(FarmHub.Group);
}
