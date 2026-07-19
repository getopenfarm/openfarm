namespace OpenFarm.Domain.FilamentTracking;

public sealed class Vendor
{
    private Vendor(Guid id, Guid organizationId, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Vendor Create(Guid organizationId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Vendor name is required.", nameof(name));
        }

        return new Vendor(Guid.NewGuid(), organizationId, name, DateTimeOffset.UtcNow);
    }
}
