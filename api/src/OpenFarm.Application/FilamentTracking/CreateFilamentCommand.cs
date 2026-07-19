namespace OpenFarm.Application.FilamentTracking;

public sealed record CreateFilamentVariantInput(string ColorName, string ColorHex);

public sealed record CreateFilamentCommand(
    Guid VendorId,
    string Material,
    string MaterialType,
    Guid? FilamentProfileId,
    IReadOnlyList<CreateFilamentVariantInput> Variants);
