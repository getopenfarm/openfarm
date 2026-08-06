namespace OpenFarm.Application.Assemblies;

public sealed record EditNonPrintedComponentCommand(Guid NonPrintedComponentId, string Name, string Unit, decimal? UnitCost);
