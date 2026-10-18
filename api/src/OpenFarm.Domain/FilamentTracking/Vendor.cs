namespace OpenFarm.Domain.FilamentTracking;

public sealed class Vendor
{
    private Vendor(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Vendor Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Vendor name is required.", nameof(name));
        }

        return new Vendor(Guid.NewGuid(), name, DateTimeOffset.UtcNow);
    }
}
