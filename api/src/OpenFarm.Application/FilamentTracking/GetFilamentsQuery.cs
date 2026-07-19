namespace OpenFarm.Application.FilamentTracking;

public sealed record GetFilamentsQuery;

public sealed record FilamentVariantSummary(Guid Id, string ColorName, string ColorHex);

public sealed record FilamentSummary(
    Guid Id,
    Guid VendorId,
    string VendorName,
    string Material,
    string MaterialType,
    Guid? FilamentProfileId,
    IReadOnlyList<FilamentVariantSummary> Variants);
