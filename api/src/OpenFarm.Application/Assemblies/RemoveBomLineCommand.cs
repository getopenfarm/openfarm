namespace OpenFarm.Application.Assemblies;

public sealed record RemoveBomLineCommand(Guid AssemblyId, Guid LineId);
