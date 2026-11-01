using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary><see cref="Lines"/> null leaves the BOM untouched (rename/redescribe only); otherwise it is a full replace. <see cref="Pricing"/> applies only when <see cref="UpdatePricing"/> (null clears it).</summary>
public sealed record EditAssemblyCommand(Guid AssemblyId, string Name, string? Description, IReadOnlyList<AssemblyLineEditInput>? Lines = null, IReadOnlyList<AssemblyCostInput>? AdditionalCosts = null, AssemblyPricing? Pricing = null, bool UpdatePricing = false);

/// <summary><see cref="LineId"/> set keeps that existing line (quantity may change); null adds a new line.</summary>
public sealed record AssemblyLineEditInput(Guid? LineId, BomLineTargetKind TargetKind, Guid TargetId, decimal Quantity);
