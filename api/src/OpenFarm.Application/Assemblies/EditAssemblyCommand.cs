namespace OpenFarm.Application.Assemblies;

public sealed record EditAssemblyCommand(Guid AssemblyId, string Name, string? Description);
