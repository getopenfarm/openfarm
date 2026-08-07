namespace OpenFarm.Application.Assemblies;

public sealed record CreateNonPrintedComponentCommand(string Name, string Unit, decimal? UnitCost);
