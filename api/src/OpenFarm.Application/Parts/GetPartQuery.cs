namespace OpenFarm.Application.Parts;

public sealed record GetPartQuery(Guid PartId);

public sealed record PartFilamentRequirementDetail(
    Guid Id,
    string? MaterialType,
    string? DetectedColorHex,
    string? DetectedSubBrand,
    Guid? NarrowFilamentVariantId,
    Guid? NarrowFilamentProfileId,
    bool NarrowFilamentVariantUnresolved,
    bool NarrowFilamentProfileUnresolved);

public sealed record PartDetail(
    Guid Id,
    string Name,
    string FileFormat,
    bool RequiresSlicing,
    string OriginalFileName,
    long FileSizeBytes,
    decimal? NozzleDiameterMm,
    string EstimateStatus,
    int? EstimatedPrintTimeMinutes,
    decimal? EstimatedFilamentWeightGrams,
    SlicerSelectionView? Slicer,
    IReadOnlyList<PartFilamentRequirementDetail> FilamentRequirements,
    int PlateCount,
    IReadOnlyList<ThumbnailView> Thumbnails,
    IReadOnlyList<string> Tags,
    int UnitsPerPrint = 1,
    string PrintRequirementsStatus = "Complete",
    IReadOnlyList<string>? ProcessingWarnings = null,
    string? EstimateUnavailableReason = null, OpenFarm.Domain.Parts.EstimationDetails? Estimation = null);
