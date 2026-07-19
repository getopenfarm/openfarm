namespace OpenFarm.Domain.Parts;

public sealed class Tag
{
    private Tag(Guid id, Guid organizationId, string name, DateTimeOffset createdAtUtc)
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

    public static Tag Create(Guid organizationId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name is required.", nameof(name));
        }

        return new Tag(Guid.NewGuid(), organizationId, name, DateTimeOffset.UtcNow);
    }
}
