namespace OpenFarm.Application.FilamentTracking;

public sealed record GetFilamentsQuery;

public sealed record FilamentVariantSummary(
    Guid Id, string ColorName, string ColorHex, decimal? PricePerKg = null, decimal? LowStockGrams = null, int? LowStockDays = null);

public sealed record FilamentSummary(
    Guid Id,
    Guid VendorId,
    string VendorName,
    string Material,
    string MaterialType,
    IReadOnlyList<FilamentProfileAssignmentInput> ProfileAssignments,
    IReadOnlyList<FilamentVariantSummary> Variants, decimal? PricePerKg = null, decimal? LowStockGrams = null, int? LowStockDays = null);
