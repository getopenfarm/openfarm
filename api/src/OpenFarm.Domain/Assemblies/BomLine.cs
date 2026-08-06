namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// One polymorphic component entry, owned by exactly one <see cref="Assembly"/> (data-model.md).
/// Targets exactly one of a catalog Part, another Assembly (a sub-assembly), or a
/// <see cref="NonPrintedComponent"/> — never more than one, never zero (FR-003).
/// </summary>
public sealed class BomLine
{
    private BomLine(
        Guid id,
        Guid organizationId,
        Guid assemblyId,
        BomLineTargetKind targetKind,
        Guid? targetPartId,
        Guid? targetAssemblyId,
        Guid? targetNonPrintedComponentId,
        decimal quantity)
    {
        var targetCount = (targetPartId is not null ? 1 : 0)
            + (targetAssemblyId is not null ? 1 : 0)
            + (targetNonPrintedComponentId is not null ? 1 : 0);

        if (targetCount != 1)
        {
            throw new ArgumentException("A BOM line must target exactly one Part, Assembly, or NonPrintedComponent.", nameof(targetKind));
        }

        var kindMatchesTarget = targetKind switch
        {
            BomLineTargetKind.Part => targetPartId is not null,
            BomLineTargetKind.Assembly => targetAssemblyId is not null,
            BomLineTargetKind.NonPrintedComponent => targetNonPrintedComponentId is not null,
            _ => false,
        };

        if (!kindMatchesTarget)
        {
            throw new ArgumentException("TargetKind does not match the target that was set.", nameof(targetKind));
        }

        Id = id;
        OrganizationId = organizationId;
        AssemblyId = assemblyId;
        TargetKind = targetKind;
        TargetPartId = targetPartId;
        TargetAssemblyId = targetAssemblyId;
        TargetNonPrintedComponentId = targetNonPrintedComponentId;
        Quantity = quantity;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid AssemblyId { get; }

    public BomLineTargetKind TargetKind { get; }

    public Guid? TargetPartId { get; }

    public Guid? TargetAssemblyId { get; }

    public Guid? TargetNonPrintedComponentId { get; }

    public decimal Quantity { get; private set; }

    internal static BomLine Create(Guid organizationId, Guid assemblyId, BomLineSpec spec)
    {
        ValidateQuantity(spec.TargetKind, spec.Quantity);

        return new BomLine(
            Guid.NewGuid(),
            organizationId,
            assemblyId,
            spec.TargetKind,
            spec.TargetPartId,
            spec.TargetAssemblyId,
            spec.TargetNonPrintedComponentId,
            spec.Quantity);
    }

    internal void UpdateQuantity(decimal quantity)
    {
        ValidateQuantity(TargetKind, quantity);
        Quantity = quantity;
    }

    /// <summary>FR-004a/FR-004b: whole number ≥ 1 for Part/Assembly targets; fractional &gt; 0 allowed for NonPrintedComponent.</summary>
    private static void ValidateQuantity(BomLineTargetKind targetKind, decimal quantity)
    {
        if (targetKind == BomLineTargetKind.NonPrintedComponent)
        {
            if (quantity <= 0)
            {
                throw new InvalidBomLineQuantityException(targetKind, quantity);
            }

            return;
        }

        if (quantity < 1 || quantity != Math.Truncate(quantity))
        {
            throw new InvalidBomLineQuantityException(targetKind, quantity);
        }
    }
}
