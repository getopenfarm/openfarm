namespace OpenFarm.Application.FilamentTracking;

public sealed record AddFilamentVariantCommand(Guid FilamentId, string ColorName, string ColorHex);
