namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// A reusable, informational catalog entry (spec Key Entities / FR-005) — screws, inserts, packaging.
/// Unitless: BOM lines count it in whole pieces, and <see cref="UnitCost"/> is the cost per piece.
/// No stock/on-hand/reorder fields of any kind (FR-005a); <see cref="UnitCost"/> is genuinely
/// optional (FR-005c).
/// </summary>
public sealed class NonPrintedComponent
{
    private NonPrintedComponent(Guid id, string name, decimal? unitCost, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        UnitCost = unitCost;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public decimal? UnitCost { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NonPrintedComponent Create(string name, decimal? unitCost)
    {
        Validate(name);
        return new NonPrintedComponent(Guid.NewGuid(), name, unitCost, DateTimeOffset.UtcNow);
    }

    public void Edit(string name, decimal? unitCost)
    {
        Validate(name);

        Name = name;
        UnitCost = unitCost;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }
    }
}
