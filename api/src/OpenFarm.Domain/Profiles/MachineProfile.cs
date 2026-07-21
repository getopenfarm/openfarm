namespace OpenFarm.Domain.Profiles;

public sealed class MachineProfile
{
    private MachineProfile(
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

    /// <summary>
    /// The process preset paired to this machine for headless slicing — Orca/Bambu Studio enforce
    /// strict machine/process compatibility (only a preset authored/shipped for this exact machine
    /// passes), so a process preset is only ever meaningful in the context of one specific machine.
    /// Set explicitly via a dedicated pairing command, never inferred at import time.
    /// </summary>
    public Guid? DefaultProcessProfileId { get; private set; }

    public static MachineProfile Create(
        Guid organizationId,
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Machine profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Machine profile raw data is required.", nameof(rawDataJson));
        }

        return new MachineProfile(
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

    public void SetDefaultProcessProfile(Guid processProfileId) => DefaultProcessProfileId = processProfileId;

    public void ClearDefaultProcessProfile() => DefaultProcessProfileId = null;
}
