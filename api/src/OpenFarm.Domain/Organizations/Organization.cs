namespace OpenFarm.Domain.Organizations;

public sealed class Organization
{
    public static readonly Guid WellKnownId = new("00000000-0000-0000-0000-000000000001");

    private Organization(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Organization Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name is required.", nameof(name));
        }

        return new Organization(WellKnownId, name, DateTimeOffset.UtcNow);
    }
}
