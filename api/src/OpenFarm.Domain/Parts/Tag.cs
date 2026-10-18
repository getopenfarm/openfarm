namespace OpenFarm.Domain.Parts;

public sealed class Tag
{
    private Tag(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Tag Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name is required.", nameof(name));
        }

        return new Tag(Guid.NewGuid(), name, DateTimeOffset.UtcNow);
    }
}
