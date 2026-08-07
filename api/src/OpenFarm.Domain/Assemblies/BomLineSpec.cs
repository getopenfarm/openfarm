namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// Input to <see cref="Assembly.Create"/>/<see cref="Assembly.AddLine"/> — a not-yet-persisted BOM
/// line. Exactly one of <see cref="TargetPartId"/>/<see cref="TargetAssemblyId"/>/
/// <see cref="TargetNonPrintedComponentId"/> must be set, matching <see cref="TargetKind"/>
/// (FR-003) — <see cref="BomLine"/>'s private constructor is the actual enforcement point.
/// </summary>
public sealed record BomLineSpec(
    BomLineTargetKind TargetKind,
    Guid? TargetPartId,
    Guid? TargetAssemblyId,
    Guid? TargetNonPrintedComponentId,
    decimal Quantity);
