namespace OpenFarm.Application.Production;

public sealed record PinPrintJobCommand(Guid JobId, Guid? PrinterId);
