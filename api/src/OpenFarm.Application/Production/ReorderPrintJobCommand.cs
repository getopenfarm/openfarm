namespace OpenFarm.Application.Production;

public sealed record ReorderPrintJobCommand(Guid JobId, Guid? AfterJobId);
