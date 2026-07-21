using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record GetPartProcessingJobQuery(Guid JobId);

public sealed record ThumbnailView(int PlateIndex, string ThumbnailBase64);

public sealed record PartProcessingJobPreviewDetail(
    decimal? NozzleDiameterMm,
    IReadOnlyList<PreviewFilamentRequirement> FilamentRequirements,
    string EstimateStatus,
    int? EstimatedPrintTimeMinutes,
    decimal? EstimatedFilamentWeightGrams,
    int? PlateCount,
    IReadOnlyList<ThumbnailView> Thumbnails);

public sealed record PartProcessingJobDetail(
    Guid Id,
    string Kind,
    string Status,
    Guid? PartId,
    PartProcessingJobPreviewDetail? Preview,
    string? ErrorMessage);
