namespace OpenFarm.Application.Parts;

public sealed record UpdateDefaultMachineProfileCommand(Guid MachineProfileId);

public sealed record DefaultMachineProfileResult(Guid MachineProfileId, string MachineProfileName, Guid? DefaultProcessProfileId);
