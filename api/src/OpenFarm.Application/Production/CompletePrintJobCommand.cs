namespace OpenFarm.Application.Production;

public sealed record CompletePrintJobCommand(Guid JobId, IReadOnlyList<FilamentConsumptionLineInput> FilamentConsumption);
