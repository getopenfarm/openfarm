namespace OpenFarm.Application.Assemblies;

/// <summary>
/// FR-009's output — never persisted, recomputed on every call. <see cref="PrintedParts"/>/
/// <see cref="NonPrintedComponents"/> are reported separately (FR-009d) so no consumer can mistake
/// one set for the other.
/// </summary>
public sealed record BomExplosionResult(
    IReadOnlyList<ExplodedPartQuantity> PrintedParts,
    IReadOnlyList<ExplodedNonPrintedComponentQuantity> NonPrintedComponents,
    IReadOnlyList<AssemblyCostContribution>? ProductionCosts = null,
    IReadOnlyList<AssemblyCostContribution>? FinalProductCosts = null);

public sealed record ExplodedPartQuantity(Guid PartId, string PartName, decimal Quantity);

public sealed record ExplodedNonPrintedComponentQuantity(Guid NonPrintedComponentId, string Name, decimal Quantity);
