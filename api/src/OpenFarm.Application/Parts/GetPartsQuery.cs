namespace OpenFarm.Application.Parts;

public sealed record GetPartsQuery(IReadOnlyList<string> Tags, int Page, int PageSize, OpenFarm.Application.Common.ListSort? Sorting = null, string? Folder = null);

public sealed record PartSummary(
    Guid Id,
    string Name,
    string FileFormat,
    bool RequiresSlicing,
    decimal? NozzleDiameterMm,
    string EstimateStatus,
    int? EstimatedPrintTimeMinutes,
    decimal? EstimatedFilamentWeightGrams,
    string? ThumbnailBase64,
    IReadOnlyList<string> MaterialTypes,
    IReadOnlyList<string> Tags,
    string PrintRequirementsStatus = "Complete",
    IReadOnlyList<string>? ProcessingWarnings = null,
    string? EstimateUnavailableReason = null, OpenFarm.Domain.Parts.EstimationDetails? Estimation = null,
    int UnitsPerPrint = 1,
    IReadOnlyList<PartColorGrams>? FilamentByColor = null);

/// <summary>One requirement's grams for the Build panel's by-colour totals (per plate set, purge included; null when unknown).</summary>
public sealed record PartColorGrams(string? MaterialType, string? ColorHex, decimal? Grams, Guid? NarrowFilamentVariantId = null);

public sealed record PartsPage(IReadOnlyList<PartSummary> Parts, int TotalCount);
