namespace OpenFarm.Application.Profiles;

public sealed record UpdateFilamentProfileRawDataCommand(Guid FilamentProfileId, string RawDataJson);
