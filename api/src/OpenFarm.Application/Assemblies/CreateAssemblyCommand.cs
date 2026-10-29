using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed record CreateAssemblyCommand(string Name, string? Description, IReadOnlyList<AssemblyLineInput> Lines, IReadOnlyList<AssemblyCostInput>? AdditionalCosts = null, AssemblyPricing? Pricing = null);

public sealed record AssemblyLineInput(BomLineTargetKind TargetKind, Guid TargetId, decimal Quantity);
