namespace OpenFarm.Application.Profiles;

public sealed record UpdateMachineProfileRawDataCommand(Guid MachineProfileId, string RawDataJson);
