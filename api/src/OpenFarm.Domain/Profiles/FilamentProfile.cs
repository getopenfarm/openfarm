namespace OpenFarm.Domain.Profiles;

public sealed class FilamentProfile
{
    private FilamentProfile(
        Guid id,
        Guid organizationId,
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        SourceFormat = sourceFormat;
        BaseProfileName = baseProfileName;
        BaseProfileId = null;
        RawDataJson = rawDataJson;
        CreatedAtUtc = createdAtUtc;
    }

    // Every property below carries a private setter, including ones this entity never mutates
    // after construction (Id, OrganizationId, Name, SourceFormat, BaseProfileName, CreatedAtUtc) —
    // EF Core's constructor-binding materialization needs a settable backing member for every
    // constructor-bound property; a pure get-only auto-property has none. External code still can't
    // mutate them (no method exposes a setter), so this doesn't weaken the entity's own invariants.
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; }
    public ProfileSourceFormat SourceFormat { get; private set; }
    public string? BaseProfileName { get; private set; }
    public Guid? BaseProfileId { get; private set; }
    public string RawDataJson { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static FilamentProfile Create(
        Guid organizationId,
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Filament profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Filament profile raw data is required.", nameof(rawDataJson));
        }

        return new FilamentProfile(
            Guid.NewGuid(),
            organizationId,
            name,
            sourceFormat,
            string.IsNullOrWhiteSpace(baseProfileName) ? null : baseProfileName,
            rawDataJson,
            DateTimeOffset.UtcNow);
    }

    // Set only once, by ConfirmProfileImportCommandHandler's Pass 2 base resolution
    // (research.md §3) — never revisited afterward, including by ReplaceRawData below.
    public void ResolveBaseProfile(Guid baseProfileId) => BaseProfileId = baseProfileId;

    // FR-012 — overwrites RawDataJson only; every descriptive field is derived from this at read
    // time (data-model.md), so there is no cached column to keep in sync here.
    public void ReplaceRawData(string rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Raw data is required.", nameof(rawDataJson));
        }

        RawDataJson = rawDataJson;
    }
}
