namespace OpenFarm.Application.Assemblies;

public sealed record UpdateBomLineQuantityCommand(Guid AssemblyId, Guid LineId, decimal Quantity);
