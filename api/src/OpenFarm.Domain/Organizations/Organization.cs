using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Organizations;

public sealed class Organization
{
    public static readonly Guid WellKnownId = new("00000000-0000-0000-0000-000000000001");

    private Organization(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        DefaultSlicerEngine = SlicerEngine.OrcaSlicer;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>The only organization-level slicing setting this phase introduces (FR-007, research.md §10).</summary>
    public SlicerEngine DefaultSlicerEngine { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Organization Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name is required.", nameof(name));
        }

        return new Organization(WellKnownId, name, DateTimeOffset.UtcNow);
    }

    public void SetDefaultSlicerEngine(SlicerEngine engine) => DefaultSlicerEngine = engine;
}
