namespace OpenFarm.Application.FilamentTracking;

public sealed record CreateFilamentVariantInput(string ColorName, string ColorHex, decimal? PricePerKg = null, decimal? LowStockGrams = null);

public sealed record CreateFilamentCommand(
    Guid VendorId,
    string Material,
    string MaterialType,
    IReadOnlyList<CreateFilamentVariantInput> Variants,
    IReadOnlyList<FilamentProfileAssignmentInput>? ProfileAssignments = null, decimal? PricePerKg = null, decimal? LowStockGrams = null);

public sealed record FilamentProfileAssignmentInput(string Brand, string ModelCode, decimal NozzleDiameterMm, Guid FilamentProfileId, string? ColorHex = null);
