using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed record CreateAssemblyCommand(string Name, string? Description, IReadOnlyList<AssemblyLineInput> Lines);

public sealed record AssemblyLineInput(BomLineTargetKind TargetKind, Guid TargetId, decimal Quantity);
