namespace OpenFarm.Application.Profiles;

public sealed record SetMachineProfileDefaultProcessCommand(Guid MachineProfileId, Guid ProcessProfileId);

public sealed record MachineProfileDefaultProcessResult(Guid MachineProfileId, Guid ProcessProfileId);
