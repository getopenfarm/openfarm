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
        ProfileOrigin origin,
        string? vendorName,
        string? externalId,
        bool isSelectable,
        IReadOnlyList<string>? compatibleMachineNames,
        string? materialType,
        IReadOnlyList<string>? includedTemplateNames,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
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
        MaterialType = materialType;
        IncludedTemplateNames = includedTemplateNames;
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

    /// <summary>User-imported vs. ingested from the bundled slicer's own vendor data (009's Origin split).</summary>
    public ProfileOrigin Origin { get; private set; }

    /// <summary>The vendor index's own <c>name</c> field (e.g. "Bambulab"); <c>null</c> for <see cref="ProfileOrigin.User"/> rows.</summary>
    public string? VendorName { get; private set; }

    /// <summary>The vendor's <c>setting_id</c> for a selectable leaf; <c>null</c> for abstract-base System rows and all User rows (research.md §1 idempotency key).</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Flips to <c>false</c> once a re-ingestion no longer contains this row's identity key (research.md §1). Always <c>true</c> for User rows.</summary>
    public bool IsCurrentlyOffered { get; private set; }

    /// <summary>From the vendor's <c>instantiation</c> flag; always <c>true</c> for User rows (unchanged import behavior).</summary>
    public bool IsSelectable { get; private set; }

    /// <summary>System rows only: verbatim <c>compatible_printers</c> array — exact machine PRESET names (research.md §0 point 2).</summary>
    public IReadOnlyList<string>? CompatibleMachineNames { get; private set; }

    /// <summary>System rows only: the resolved (chain-merged) <c>filament_type</c> (e.g. "PLA"/"PETG"/"ABS") — cached at ingestion time, not re-derived per read (research.md §0 point 5).</summary>
    public string? MaterialType { get; private set; }

    /// <summary>System rows only, verbatim from the vendor leaf's own <c>include</c> array (Bambu Studio's gcode-template fold-in, research.md §0a); <c>null</c> otherwise.</summary>
    public IReadOnlyList<string>? IncludedTemplateNames { get; private set; }

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
            ProfileOrigin.User,
            vendorName: null,
            externalId: null,
            isSelectable: true,
            compatibleMachineNames: null,
            materialType: null,
            includedTemplateNames: null,
            DateTimeOffset.UtcNow);
    }

    /// <summary>Ingestion-only factory (research.md §0/§1) — never invoked from any User-facing import path.</summary>
    public static FilamentProfile CreateSystemProfile(
        Guid organizationId,
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson,
        string vendorName,
        string? externalId,
        bool isSelectable,
        IReadOnlyList<string>? compatibleMachineNames,
        string? materialType,
        IReadOnlyList<string>? includedTemplateNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Filament profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Filament profile raw data is required.", nameof(rawDataJson));
        }

        if (string.IsNullOrWhiteSpace(vendorName))
        {
            throw new ArgumentException("Vendor name is required for a System profile.", nameof(vendorName));
        }

        return new FilamentProfile(
            Guid.NewGuid(),
            organizationId,
            name,
            sourceFormat,
            string.IsNullOrWhiteSpace(baseProfileName) ? null : baseProfileName,
            rawDataJson,
            ProfileOrigin.System,
            vendorName,
            externalId,
            isSelectable,
            compatibleMachineNames,
            materialType,
            includedTemplateNames,
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

    /// <summary>
    /// Re-ingestion upsert (research.md §1): refreshes every derived field from the newly-fetched
    /// vendor data in place — same row <see cref="Id"/>, so a part's narrowed filament FK stays valid
    /// — and marks the row as currently offered again (it was just re-seen this run).
    /// </summary>
    public void RefreshFromSystemSource(
        string name,
        string rawDataJson,
        bool isSelectable,
        IReadOnlyList<string>? compatibleMachineNames,
        string? materialType,
        IReadOnlyList<string>? includedTemplateNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Filament profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Filament profile raw data is required.", nameof(rawDataJson));
        }

        Name = name;
        RawDataJson = rawDataJson;
        IsSelectable = isSelectable;
        CompatibleMachineNames = compatibleMachineNames;
        MaterialType = materialType;
        IncludedTemplateNames = includedTemplateNames;
        IsCurrentlyOffered = true;
    }

    /// <summary>A previously-System row not re-seen this ingestion run (research.md §1) — never deleted, just hidden from pickers.</summary>
    public void MarkStale() => IsCurrentlyOffered = false;
}
