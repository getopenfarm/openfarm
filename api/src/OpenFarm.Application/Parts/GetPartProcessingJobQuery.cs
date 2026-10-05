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
    IReadOnlyList<ThumbnailView> Thumbnails,
    string PrintRequirementsStatus = "Complete",
    IReadOnlyList<string>? ProcessingWarnings = null,
    string? EstimateUnavailableReason = null, OpenFarm.Domain.Parts.EstimationDetails? Estimation = null);

public sealed record PartProcessingJobDetail(
    Guid Id,
    string Kind,
    string Status,
    Guid? PartId,
    SlicerSelectionView? Slicer,
    PartProcessingJobPreviewDetail? Preview,
    string? ErrorMessage);
