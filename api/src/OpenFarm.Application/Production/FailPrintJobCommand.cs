namespace OpenFarm.Application.Production;

public sealed record FailPrintJobCommand(Guid JobId, IReadOnlyList<FilamentConsumptionLineInput> FilamentConsumption);
