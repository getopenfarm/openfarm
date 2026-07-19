namespace OpenFarm.Application.Parts;

public sealed record GetPartsQuery(IReadOnlyList<string> Tags, int Page, int PageSize);

public sealed record PartSummary(
    Guid Id,
    string Name,
    string FileFormat,
    bool RequiresSlicing,
    decimal NozzleDiameterMm,
    string EstimateStatus,
    int? EstimatedPrintTimeMinutes,
    decimal? EstimatedFilamentWeightGrams,
    string? ThumbnailBase64,
    IReadOnlyList<string> MaterialTypes,
    IReadOnlyList<string> Tags);

public sealed record PartsPage(IReadOnlyList<PartSummary> Parts, int TotalCount);
