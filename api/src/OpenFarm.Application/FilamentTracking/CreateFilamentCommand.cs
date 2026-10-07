namespace OpenFarm.Application.FilamentTracking;

public sealed record CreateFilamentVariantInput(string ColorName, string ColorHex);

public sealed record CreateFilamentCommand(
    Guid VendorId,
    string Material,
    string MaterialType,
    IReadOnlyList<CreateFilamentVariantInput> Variants,
    IReadOnlyList<FilamentProfileAssignmentInput>? ProfileAssignments = null);

public sealed record FilamentProfileAssignmentInput(string Brand, string ModelCode, decimal NozzleDiameterMm, Guid FilamentProfileId, string? ColorHex = null);
