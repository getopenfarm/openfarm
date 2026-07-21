namespace OpenFarm.Application.Printers;

public sealed record LinkPrinterMachineProfileCommand(Guid PrinterId, Guid? MachineProfileId);
