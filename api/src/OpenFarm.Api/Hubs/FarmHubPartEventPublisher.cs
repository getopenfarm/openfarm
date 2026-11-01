using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubPartEventPublisher(IFarmEventClients clients, IPartFileStorage? fileStorage = null) : IPartEventPublisher
{
    public async Task PartProcessingJobStatusChangedAsync(PartProcessingJob job, CancellationToken cancellationToken)
    {
        var quantities = job.Status == PartProcessingJobStatus.ReadyForReview
            ? await PartProcessingQuantity.ReadAsync(job, fileStorage, cancellationToken) : null;
        await Group.SendAsync(
            "PartProcessingJobStatusChanged",
            new
            {
                jobId = job.Id,
                status = job.Status.ToString(),
                partId = job.PartId,
                errorMessage = job.ErrorMessage,
                preview = quantities is not null ? BuildPreview(job, quantities) : null,
            },
            cancellationToken);
    }

    public Task PartUpsertedAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartUpserted", new { partId }, cancellationToken);

    public Task PartEstimateResolvedAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartEstimateResolved", new { partId }, cancellationToken);

    public Task PartEstimateUnavailableAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartEstimateUnavailable", new { partId }, cancellationToken);

    public Task PartDeletedAsync(Guid partId, CancellationToken cancellationToken) =>
        Group.SendAsync("PartDeleted", new { partId }, cancellationToken);

    private static object BuildPreview(PartProcessingJob job, PartProcessingQuantity quantities) => new
    {
        unitsPerPrint = quantities.UnitsPerPrint,
        nozzleDiameterMm = job.PreviewNozzleDiameterMm,
        filamentRequirements = job.PreviewFilamentRequirements.Select(Requirement),
        estimateStatus = job.PreviewEstimateStatus.ToString(),
        estimatedPrintTimeMinutes = job.PreviewEstimatedPrintTimeMinutes,
        estimatedFilamentWeightGrams = job.PreviewEstimatedFilamentWeightGrams,
        plates = job.PreviewPlates.Select(p => new
        {
            unitsPerPrint = quantities.Plates.GetValueOrDefault(p.Number),
            number = p.Number,
            name = p.Name,
            estimatedPrintTimeMinutes = p.EstimatedPrintTimeMinutes,
            estimatedFilamentWeightGrams = p.EstimatedFilamentWeightGrams,
            filamentRequirements = job.PlateRequirements(p).Select(Requirement),
        }),
        thumbnails = job.PreviewThumbnails.Select(t => new
        {
            plateIndex = t.PlateIndex,
            thumbnailBase64 = Convert.ToBase64String(t.Bytes),
        }),
    };

    private static object Requirement(PreviewFilamentRequirement r) => new
    {
        requirementIndex = r.RequirementIndex,
        materialType = r.MaterialType,
        detectedColorHex = r.DetectedColorHex,
        detectedSubBrand = r.DetectedSubBrand,
        narrowFilamentVariantId = r.NarrowFilamentVariantId,
        narrowFilamentProfileId = r.NarrowFilamentProfileId,
        estimatedGrams = r.EstimatedGrams,
    };

    private IClientProxy Group => clients;
}
