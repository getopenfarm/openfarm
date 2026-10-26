using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record GetPartProcessingJobQuery(Guid JobId);

public sealed record ThumbnailView(int PlateIndex, string ThumbnailBase64);

/// <summary>One plate the file offers: its own estimate and the requirements it prints with (grams from it alone).</summary>
public sealed record PlateView(int Number, string? Name, int? EstimatedPrintTimeMinutes, decimal? EstimatedFilamentWeightGrams,
    IReadOnlyList<PreviewFilamentRequirement> FilamentRequirements);

public sealed record PartProcessingJobPreviewDetail(
    decimal? NozzleDiameterMm,
    IReadOnlyList<PreviewFilamentRequirement> FilamentRequirements,
    string EstimateStatus,
    int? EstimatedPrintTimeMinutes,
    decimal? EstimatedFilamentWeightGrams,
    IReadOnlyList<PlateView> Plates,
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
