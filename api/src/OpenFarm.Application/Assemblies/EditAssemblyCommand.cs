using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary><see cref="Lines"/> null leaves the BOM untouched (rename/redescribe only); otherwise it is a full replace.</summary>
public sealed record EditAssemblyCommand(Guid AssemblyId, string Name, string? Description, IReadOnlyList<AssemblyLineEditInput>? Lines = null);

/// <summary><see cref="LineId"/> set keeps that existing line (quantity may change); null adds a new line.</summary>
public sealed record AssemblyLineEditInput(Guid? LineId, BomLineTargetKind TargetKind, Guid TargetId, decimal Quantity);
