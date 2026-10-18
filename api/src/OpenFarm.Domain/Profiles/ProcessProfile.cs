namespace OpenFarm.Domain.Profiles;

public sealed class ProcessProfile
{
    private ProcessProfile(
        Guid id,
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson,
        ProfileOrigin origin,
        string? vendorName,
        string? externalId,
        bool isSelectable,
        IReadOnlyList<string>? compatibleMachineNames,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        SourceFormat = sourceFormat;
        BaseProfileName = baseProfileName;
        BaseProfileId = null;
        RawDataJson = rawDataJson;
        Origin = origin;
        VendorName = vendorName;
        ExternalId = externalId;
        IsCurrentlyOffered = true;
        IsSelectable = isSelectable;
        CompatibleMachineNames = compatibleMachineNames;
        CreatedAtUtc = createdAtUtc;
    }

    // Every property below carries a private setter, including ones this entity never mutates
    // after construction (Id, Name, SourceFormat, BaseProfileName, CreatedAtUtc) —
    // EF Core's constructor-binding materialization needs a settable backing member for every
    // constructor-bound property; a pure get-only auto-property has none. External code still can't
    // mutate them (no method exposes a setter), so this doesn't weaken the entity's own invariants.
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ProfileSourceFormat SourceFormat { get; private set; }
    public string? BaseProfileName { get; private set; }
    public Guid? BaseProfileId { get; private set; }
    public string RawDataJson { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Always <see cref="ProfileOrigin.System"/> after 009 (FR-011 removes the import path); kept for symmetry with Machine/Filament so a picker query is one shape across all three kinds.</summary>
    public ProfileOrigin Origin { get; private set; }

    /// <summary>The vendor index's own <c>name</c> field (e.g. "Bambulab"); <c>null</c> for any pre-existing <see cref="ProfileOrigin.User"/> row.</summary>
    public string? VendorName { get; private set; }

    /// <summary>The vendor's <c>setting_id</c> for a selectable leaf; <c>null</c> for abstract-base rows (research.md §1 idempotency key).</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Flips to <c>false</c> once a re-ingestion no longer contains this row's identity key (research.md §1).</summary>
    public bool IsCurrentlyOffered { get; private set; }

    /// <summary>From the vendor's <c>instantiation</c> flag.</summary>
    public bool IsSelectable { get; private set; }

    /// <summary>System rows only: verbatim <c>compatible_printers</c> array — exact machine PRESET names, not model names (research.md §0 point 2, FR-006).</summary>
    public IReadOnlyList<string>? CompatibleMachineNames { get; private set; }

    /// <summary>Ingestion-only factory (research.md §0/§1) — process profiles are never created by any other path after 009 (FR-011).</summary>
    public static ProcessProfile CreateSystemProfile(
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson,
        string vendorName,
        string? externalId,
        bool isSelectable,
        IReadOnlyList<string>? compatibleMachineNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Process profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Process profile raw data is required.", nameof(rawDataJson));
        }

        if (string.IsNullOrWhiteSpace(vendorName))
        {
            throw new ArgumentException("Vendor name is required for a System profile.", nameof(vendorName));
        }

        return new ProcessProfile(
            Guid.NewGuid(),
            name,
            sourceFormat,
            string.IsNullOrWhiteSpace(baseProfileName) ? null : baseProfileName,
            rawDataJson,
            ProfileOrigin.System,
            vendorName,
            externalId,
            isSelectable,
            compatibleMachineNames,
            DateTimeOffset.UtcNow);
    }

    // Set only once, by ConfirmProfileImportCommandHandler's Pass 2 base resolution
    // (research.md §3) — never revisited afterward, including by ReplaceRawData below.
    public void ResolveBaseProfile(Guid baseProfileId) => BaseProfileId = baseProfileId;

    // Ingestion re-runs use this to refresh a System row's JSON on upsert (research.md §1). No
    // endpoint calls this for a User-initiated edit — process profiles have no raw-editor path (FR-011).
    public void ReplaceRawData(string rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Raw data is required.", nameof(rawDataJson));
        }

        RawDataJson = rawDataJson;
    }

    /// <summary>
    /// Re-ingestion upsert (research.md §1): refreshes every derived field from the newly-fetched
    /// vendor data in place — same row <see cref="Id"/>, so a printer's <c>ProcessProfileId</c> FK
    /// stays valid — and marks the row as currently offered again (it was just re-seen this run).
    /// </summary>
    public void RefreshFromSystemSource(
        string name, string rawDataJson, bool isSelectable, IReadOnlyList<string>? compatibleMachineNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Process profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Process profile raw data is required.", nameof(rawDataJson));
        }

        Name = name;
        RawDataJson = rawDataJson;
        IsSelectable = isSelectable;
        CompatibleMachineNames = compatibleMachineNames;
        IsCurrentlyOffered = true;
    }

    /// <summary>A previously-System row not re-seen this ingestion run (research.md §1) — never deleted, just hidden from pickers.</summary>
    public void MarkStale() => IsCurrentlyOffered = false;
}
