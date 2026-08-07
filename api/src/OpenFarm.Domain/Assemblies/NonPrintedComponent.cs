namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// A reusable, informational catalog entry (spec Key Entities / FR-005) — screws, inserts, packaging.
/// No stock/on-hand/reorder fields of any kind (FR-005a); <see cref="UnitCost"/> is genuinely
/// optional (FR-005c).
/// </summary>
public sealed class NonPrintedComponent
{
    private NonPrintedComponent(Guid id, Guid organizationId, string name, string unit, decimal? unitCost, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Unit = unit;
        UnitCost = unitCost;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public string Name { get; private set; }

    public string Unit { get; private set; }

    public decimal? UnitCost { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NonPrintedComponent Create(Guid organizationId, string name, string unit, decimal? unitCost)
    {
        Validate(name, unit);
        return new NonPrintedComponent(Guid.NewGuid(), organizationId, name, unit, unitCost, DateTimeOffset.UtcNow);
    }

    public void Edit(string name, string unit, decimal? unitCost)
    {
        Validate(name, unit);

        Name = name;
        Unit = unit;
        UnitCost = unitCost;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void Validate(string name, string unit)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException("Unit is required.", nameof(unit));
        }
    }
}
