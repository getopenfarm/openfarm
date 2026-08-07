namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-004a/FR-004b: quantity doesn't satisfy the rule for its target kind (whole number ≥ 1 for Part/Assembly, &gt; 0 for NonPrintedComponent).</summary>
public sealed class InvalidBomLineQuantityException(BomLineTargetKind targetKind, decimal quantity)
    : Exception($"Quantity {quantity} is not valid for a {targetKind} BOM line.")
{
    public BomLineTargetKind TargetKind { get; } = targetKind;

    public decimal Quantity { get; } = quantity;
}
