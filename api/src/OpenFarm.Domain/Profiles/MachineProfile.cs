namespace OpenFarm.Domain.Profiles;

public sealed class MachineProfile : ICatalogProfile
{
    private MachineProfile(
        Guid id,
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson,
        ProfileOrigin origin,
        string? vendorName,
        string? externalId,
        bool isSelectable,
        string? vendorMachineModelName,
        IReadOnlyList<string>? includedTemplateNames,
        string? sourceVersion,
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
        VendorMachineModelName = vendorMachineModelName;
        IncludedTemplateNames = includedTemplateNames;
        SourceVersion = sourceVersion;
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

    /// <summary>User-imported vs. ingested from the bundled slicer's own vendor data (009's Origin split).</summary>
    public ProfileOrigin Origin { get; private set; }

    /// <summary>The vendor index's own <c>name</c> field (e.g. "Bambulab"); <c>null</c> for <see cref="ProfileOrigin.User"/> rows.</summary>
    public string? VendorName { get; private set; }

    /// <summary>The vendor's <c>setting_id</c> for a selectable leaf; <c>null</c> for abstract-base System rows and all User rows (research.md §1 idempotency key).</summary>
    public string? ExternalId { get; private set; }

    /// <summary>The slicer version (bundle leaf directory name, <c>SlicerRef.Version</c>) a System row was ingested from; always set for System rows, <c>null</c> for User rows.</summary>
    public string? SourceVersion { get; private set; }

    /// <summary>Flips to <c>false</c> once a re-ingestion no longer contains this row's identity key (research.md §1). Always <c>true</c> for User rows.</summary>
    public bool IsCurrentlyOffered { get; private set; }

    /// <summary>From the vendor's <c>instantiation</c> flag; always <c>true</c> for User rows (unchanged import behavior).</summary>
    public bool IsSelectable { get; private set; }

    /// <summary>System machine rows only: the leaf preset's own <c>printer_model</c>, linking to <c>PrinterModelCatalog.VendorMachineModelName</c>.</summary>
    public string? VendorMachineModelName { get; private set; }

    /// <summary>System rows only, verbatim from the vendor leaf's own <c>include</c> array (Bambu Studio's gcode-template fold-in, research.md §0a); <c>null</c> otherwise.</summary>
    public IReadOnlyList<string>? IncludedTemplateNames { get; private set; }

    public static MachineProfile Create(
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
            name,
            sourceFormat,
            string.IsNullOrWhiteSpace(baseProfileName) ? null : baseProfileName,
            rawDataJson,
            ProfileOrigin.User,
            vendorName: null,
            externalId: null,
            isSelectable: true,
            vendorMachineModelName: null,
            includedTemplateNames: null,
            sourceVersion: null,
            createdAtUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>Ingestion-only factory (research.md §0/§1) — never invoked from any User-facing import path.</summary>
    public static MachineProfile CreateSystemProfile(
        string name,
        ProfileSourceFormat sourceFormat,
        string? baseProfileName,
        string rawDataJson,
        string vendorName,
        string? externalId,
        bool isSelectable,
        string? vendorMachineModelName,
        IReadOnlyList<string>? includedTemplateNames,
        string sourceVersion)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Machine profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Machine profile raw data is required.", nameof(rawDataJson));
        }

        if (string.IsNullOrWhiteSpace(vendorName))
        {
            throw new ArgumentException("Vendor name is required for a System profile.", nameof(vendorName));
        }

        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            throw new ArgumentException("Source version is required for a System profile.", nameof(sourceVersion));
        }

        return new MachineProfile(
            Guid.NewGuid(),
            name,
            sourceFormat,
            string.IsNullOrWhiteSpace(baseProfileName) ? null : baseProfileName,
            rawDataJson,
            ProfileOrigin.System,
            vendorName,
            externalId,
            isSelectable,
            vendorMachineModelName,
            includedTemplateNames,
            sourceVersion,
            DateTimeOffset.UtcNow);
    }

    // Set by ConfirmProfileImportCommandHandler's Pass 2 base resolution (research.md §3) and, for System
    // rows, by every ingestion run's own Pass 2 — never touched by ReplaceRawData below.
    public void ResolveBaseProfile(Guid baseProfileId) => BaseProfileId = baseProfileId;

    /// <summary>Ingestion only: the declared base is no longer in the catalog (or was dropped), so the stored link must not point at a stale parent.</summary>
    public void ClearBaseProfile() => BaseProfileId = null;

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
    /// vendor data in place — same row <see cref="Id"/>, so a printer/part FK pointing at it stays
    /// valid — and marks the row as currently offered again (it was just re-seen this run).
    /// </summary>
    public void RefreshFromSystemSource(
        string name, string? baseProfileName, string rawDataJson, bool isSelectable, string? vendorMachineModelName, IReadOnlyList<string>? includedTemplateNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Machine profile name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            throw new ArgumentException("Machine profile raw data is required.", nameof(rawDataJson));
        }

        Name = name;
        BaseProfileName = baseProfileName;
        RawDataJson = rawDataJson;
        IsSelectable = isSelectable;
        VendorMachineModelName = vendorMachineModelName;
        IncludedTemplateNames = includedTemplateNames;
        IsCurrentlyOffered = true;
    }

    /// <summary>A previously-System row not re-seen this ingestion run (research.md §1) — never deleted, just hidden from pickers.</summary>
    public void MarkStale() => IsCurrentlyOffered = false;
}
