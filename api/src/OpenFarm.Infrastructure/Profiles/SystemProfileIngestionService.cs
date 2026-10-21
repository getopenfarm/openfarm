using Microsoft.Extensions.Logging;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Profiles;

/// <summary>
/// Ingests a single bundled slicer leaf's vendor profile data into <c>Origin=System</c> rows
/// (research.md §0/§1, contracts/system-profile-ingestion.md). Scopes vendors by cross-referencing
/// each vendor index's own <c>machine_model_list</c> against <see cref="PrinterModelCatalog.AllVendorMachineModelNames"/>
/// (FR-001) — never a hardcoded vendor name, so a second vendor's rows appear automatically once the
/// catalog gains that vendor's models. A malformed/unreadable vendor file or referenced preset is
/// skipped with a logged warning; it never aborts the rest of the run (the contract's failure-handling
/// rule, mirrored from <c>Provisioner.ProvisionAsync</c>'s own per-entry reporting).
/// <para>
/// Every leaf is its own catalog: rows carry the leaf's slicer version (its directory name), and a run only
/// reads, updates and stale-marks the rows of that one (engine, version) — other versions are never touched.
/// The catalog is loaded once, upserted in memory and committed by a single save, so a crash leaves either
/// the whole leaf or nothing.
/// </para>
/// </summary>
public sealed partial class SystemProfileIngestionService(
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IVendorRepository vendorRepository,
    VendorProfileIndexReader indexReader,
    ILogger<SystemProfileIngestionService> logger) : ISystemProfileIngestionService
{
    // OrcaSlicer ships generic filament bases in this vendor-less index; other vendors' filaments inherit from it.
    private const string OrcaFilamentLibraryName = "OrcaFilamentLibrary";

    private static readonly Dictionary<string, SlicerEngine> EngineSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orcaslicer"] = SlicerEngine.OrcaSlicer,
        ["bambustudio"] = SlicerEngine.BambuStudio,
    };

    public async Task<IngestionSummary> IngestAsync(string bundleLeafPath, CancellationToken cancellationToken)
    {
        if (ResolveCatalog(bundleLeafPath) is not var (sourceFormat, sourceVersion))
        {
            LogUnresolvableEngine(logger, bundleLeafPath);
            return EmptySummary();
        }

        var vendorIndexFiles = ResourcesProfilesLocator.FindVendorIndexFiles(bundleLeafPath);
        if (vendorIndexFiles.Count == 0)
        {
            LogNoVendorIndexFilesFound(logger, bundleLeafPath);
            return EmptySummary();
        }

        var vendorsScanned = 0;
        var vendorsSkipped = 0;
        var machineCounts = new MutableCounts();
        var processCounts = new MutableCounts();
        var filamentCounts = new MutableCounts();
        var filamentVendorNamesSeen = new HashSet<string>(StringComparer.Ordinal);

        var machines = new Catalog<MachineProfile>(
            await machineProfileRepository.ListSystemCatalogAsync(sourceFormat, sourceVersion, cancellationToken),
            row => row.VendorName, row => row.ExternalId, row => row.Name, row => row.Id);
        var processes = new Catalog<ProcessProfile>(
            await processProfileRepository.ListSystemCatalogAsync(sourceFormat, sourceVersion, cancellationToken),
            row => row.VendorName, row => row.ExternalId, row => row.Name, row => row.Id);
        var filaments = new Catalog<FilamentProfile>(
            await filamentProfileRepository.ListSystemCatalogAsync(sourceFormat, sourceVersion, cancellationToken),
            row => row.VendorName, row => row.ExternalId, row => row.Name, row => row.Id);

        var library = sourceFormat == ProfileSourceFormat.OrcaSlicer ? ReadFilamentLibrary(vendorIndexFiles) : null;

        foreach (var vendorIndexFile in vendorIndexFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            VendorIndexSummary summary;
            try
            {
                summary = VendorProfileIndexReader.ReadSummary(vendorIndexFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                LogSkippedVendorIndex(logger, vendorIndexFile, ex);
                vendorsSkipped++;
                continue;
            }

            var isSupported = summary.MachineModelNames
                .Intersect(PrinterModelCatalog.AllVendorMachineModelNames, StringComparer.Ordinal)
                .Any();
            if (!isSupported)
            {
                LogSkippedUnsupportedVendor(logger, summary.VendorName, vendorIndexFile);
                vendorsSkipped++;
                continue;
            }

            vendorsScanned++;

            var machineResult = indexReader.ReadPresets(vendorIndexFile, ProfileKind.Machine);
            var processResult = indexReader.ReadPresets(vendorIndexFile, ProfileKind.Process);
            var filamentResult = indexReader.ReadPresets(vendorIndexFile, ProfileKind.Filament, library);

            foreach (var skipped in machineResult.SkippedEntries.Concat(processResult.SkippedEntries).Concat(filamentResult.SkippedEntries))
            {
                LogSkippedPresetEntry(logger, skipped, summary.VendorName);
            }

            var machineByName = UpsertMachine(summary.VendorName, sourceFormat, sourceVersion, machineResult.Presets, machines, machineCounts);
            var processByName = UpsertProcess(summary.VendorName, sourceFormat, sourceVersion, processResult.Presets, processes, processCounts);
            // Library bases this vendor's filaments inherit from are stored as hidden rows of their own, so a chain
            // that leaves the vendor still resolves (and slices) instead of ending at an unknown parent.
            Dictionary<string, FilamentProfile>? libraryByName = null;
            if (library is not null && LibraryClosure(filamentResult.Presets, library) is { Count: > 0 } closure)
            {
                libraryByName = UpsertFilament(OrcaFilamentLibraryName, sourceFormat, sourceVersion, closure, filaments, filamentCounts);
            }

            var filamentByName = UpsertFilament(summary.VendorName, sourceFormat, sourceVersion, filamentResult.Presets, filaments, filamentCounts);
            foreach (var filamentProfile in filamentByName.Values)
            {
                if (filamentProfile.VendorName is { } filamentVendorName)
                {
                    filamentVendorNamesSeen.Add(filamentVendorName);
                }
            }

            ResolveMachineBaseIds(machineByName);
            ResolveProcessBaseIds(processByName);
            if (libraryByName is not null)
            {
                ResolveFilamentBaseIds(libraryByName, null);
            }

            ResolveFilamentBaseIds(filamentByName, libraryByName);
        }

        // Nothing supported was read, so there is no evidence any row went away (a changed bundle layout must
        // not hide the whole catalog): only mark rows stale once at least one vendor was actually scanned.
        if (vendorsScanned > 0)
        {
            machineCounts.MarkedStale = machines.MarkUnseenStale(row => row.IsCurrentlyOffered, row => row.MarkStale());
            processCounts.MarkedStale = processes.MarkUnseenStale(row => row.IsCurrentlyOffered, row => row.MarkStale());
            filamentCounts.MarkedStale = filaments.MarkUnseenStale(row => row.IsCurrentlyOffered, row => row.MarkStale());
        }

        // The three repositories share one DbContext, so the first save commits the whole leaf in one transaction.
        await machineProfileRepository.SaveChangesAsync(cancellationToken);
        await processProfileRepository.SaveChangesAsync(cancellationToken);
        await filamentProfileRepository.SaveChangesAsync(cancellationToken);

        await EnsureVendorsExistAsync(filamentVendorNamesSeen, cancellationToken);

        return new IngestionSummary(
            vendorsScanned,
            vendorsSkipped,
            machineCounts.ToImmutable(),
            processCounts.ToImmutable(),
            filamentCounts.ToImmutable());
    }

    private Dictionary<string, MachineProfile> UpsertMachine(
        string vendorName, ProfileSourceFormat sourceFormat, string sourceVersion,
        IReadOnlyList<ParsedVendorPreset> presets, Catalog<MachineProfile> catalog, MutableCounts counts)
    {
        var byName = new Dictionary<string, MachineProfile>(StringComparer.Ordinal);
        var colliding = FindCollidingExternalIds("machine", presets, _ => vendorName);

        foreach (var preset in presets)
        {
            var externalId = KeyExternalId(preset, vendorName, colliding);
            var key = IdentityKey.For(vendorName, externalId, preset.Name);

            if (catalog.Index.TryGetValue(key, out var existing))
            {
                var changed = existing.Name != preset.Name
                    || existing.BaseProfileName != preset.BaseProfileName
                    || !JsonSemanticEquality.Equal(existing.RawDataJson, preset.RawJson)
                    || existing.IsSelectable != preset.IsSelectable
                    || existing.VendorMachineModelName != preset.VendorMachineModelName
                    || !SequenceEqualOrBothNull(existing.IncludedTemplateNames, preset.IncludedTemplateNames);

                existing.RefreshFromSystemSource(preset.Name, preset.BaseProfileName, preset.RawJson, preset.IsSelectable, preset.VendorMachineModelName, preset.IncludedTemplateNames);
                if (changed)
                {
                    counts.Updated++;
                }

                catalog.Seen.Add(existing.Id);
                byName[preset.Name] = existing;
                continue;
            }

            var created = MachineProfile.CreateSystemProfile(
                preset.Name, sourceFormat, preset.BaseProfileName, preset.RawJson,
                vendorName, externalId, preset.IsSelectable, preset.VendorMachineModelName, preset.IncludedTemplateNames, sourceVersion);
            machineProfileRepository.Stage(created);
            catalog.Index[key] = created;
            counts.Created++;
            catalog.Seen.Add(created.Id);
            byName[preset.Name] = created;
        }

        return byName;
    }

    private Dictionary<string, ProcessProfile> UpsertProcess(
        string vendorName, ProfileSourceFormat sourceFormat, string sourceVersion,
        IReadOnlyList<ParsedVendorPreset> presets, Catalog<ProcessProfile> catalog, MutableCounts counts)
    {
        var byName = new Dictionary<string, ProcessProfile>(StringComparer.Ordinal);
        var colliding = FindCollidingExternalIds("process", presets, _ => vendorName);

        foreach (var preset in presets)
        {
            var externalId = KeyExternalId(preset, vendorName, colliding);
            var key = IdentityKey.For(vendorName, externalId, preset.Name);

            if (catalog.Index.TryGetValue(key, out var existing))
            {
                var changed = existing.Name != preset.Name
                    || existing.BaseProfileName != preset.BaseProfileName
                    || !JsonSemanticEquality.Equal(existing.RawDataJson, preset.RawJson)
                    || existing.IsSelectable != preset.IsSelectable
                    || !SequenceEqualOrBothNull(existing.CompatibleMachineNames, preset.CompatibleMachineNames);

                existing.RefreshFromSystemSource(preset.Name, preset.BaseProfileName, preset.RawJson, preset.IsSelectable, preset.CompatibleMachineNames);
                if (changed)
                {
                    counts.Updated++;
                }

                catalog.Seen.Add(existing.Id);
                byName[preset.Name] = existing;
                continue;
            }

            var created = ProcessProfile.CreateSystemProfile(
                preset.Name, sourceFormat, preset.BaseProfileName, preset.RawJson,
                vendorName, externalId, preset.IsSelectable, preset.CompatibleMachineNames, sourceVersion);
            processProfileRepository.Stage(created);
            catalog.Index[key] = created;
            counts.Created++;
            catalog.Seen.Add(created.Id);
            byName[preset.Name] = created;
        }

        return byName;
    }

    private Dictionary<string, FilamentProfile> UpsertFilament(
        string vendorName, ProfileSourceFormat sourceFormat, string sourceVersion,
        IReadOnlyList<ParsedVendorPreset> presets, Catalog<FilamentProfile> catalog, MutableCounts counts)
    {
        var byName = new Dictionary<string, FilamentProfile>(StringComparer.Ordinal);

        // Falls back to the vendor index's own name only if a preset's ancestor chain never declares
        // filament_vendor — the resolved per-preset brand (research.md §0, VendorProfileIndexReader)
        // is what a real bundle actually means, since one filament index file bundles many brands.
        string ResolvedVendor(ParsedVendorPreset preset) => preset.VendorName ?? vendorName;
        var colliding = FindCollidingExternalIds("filament", presets, ResolvedVendor);

        foreach (var preset in presets)
        {
            var resolvedVendorName = ResolvedVendor(preset);
            var externalId = KeyExternalId(preset, resolvedVendorName, colliding);
            var key = IdentityKey.For(resolvedVendorName, externalId, preset.Name);

            if (catalog.Index.TryGetValue(key, out var existing))
            {
                var changed = existing.Name != preset.Name
                    || existing.BaseProfileName != preset.BaseProfileName
                    || !JsonSemanticEquality.Equal(existing.RawDataJson, preset.RawJson)
                    || existing.IsSelectable != preset.IsSelectable
                    || existing.MaterialType != preset.MaterialType
                    || !SequenceEqualOrBothNull(existing.CompatibleMachineNames, preset.CompatibleMachineNames)
                    || !SequenceEqualOrBothNull(existing.IncludedTemplateNames, preset.IncludedTemplateNames);

                existing.RefreshFromSystemSource(
                    preset.Name, preset.BaseProfileName, preset.RawJson, preset.IsSelectable, preset.CompatibleMachineNames, preset.MaterialType, preset.IncludedTemplateNames);
                if (changed)
                {
                    counts.Updated++;
                }

                catalog.Seen.Add(existing.Id);
                byName[preset.Name] = existing;
                continue;
            }

            var created = FilamentProfile.CreateSystemProfile(
                preset.Name, sourceFormat, preset.BaseProfileName, preset.RawJson,
                resolvedVendorName, externalId, preset.IsSelectable, preset.CompatibleMachineNames, preset.MaterialType, preset.IncludedTemplateNames, sourceVersion);
            filamentProfileRepository.Stage(created);
            catalog.Index[key] = created;
            counts.Created++;
            catalog.Seen.Add(created.Id);
            byName[preset.Name] = created;
        }

        return byName;
    }

    // A setting_id shared by several presets of one vendor cannot be their identity: the unique key would
    // collapse them into one row, and which one survived would depend on read order. Every member of such a
    // group is keyed by name instead (and stored without an ExternalId), so the outcome never depends on order.
    private HashSet<(string Vendor, string ExternalId)> FindCollidingExternalIds(
        string kind, IReadOnlyList<ParsedVendorPreset> presets, Func<ParsedVendorPreset, string> vendorOf)
    {
        var colliding = new HashSet<(string Vendor, string ExternalId)>();
        foreach (var group in presets
            .Where(preset => preset.ExternalId is not null)
            .GroupBy(preset => (Vendor: vendorOf(preset), ExternalId: preset.ExternalId!))
            .Where(group => group.Count() > 1))
        {
            colliding.Add(group.Key);
            LogCollidingExternalId(logger, group.Key.ExternalId, group.Count(), kind, group.Key.Vendor);
        }

        return colliding;
    }

    private static string? KeyExternalId(ParsedVendorPreset preset, string vendorName, HashSet<(string Vendor, string ExternalId)> colliding) =>
        preset.ExternalId is { } externalId && colliding.Contains((vendorName, externalId)) ? null : preset.ExternalId;

    // Pass 2 — sets (or clears) BaseProfileId purely within this vendor's own just-upserted batch: research.md
    // §0 confirms every ancestor a leaf declares via "inherits" is itself present in the same vendor
    // file, so no cross-vendor or cross-run lookup is ever needed. Clearing matters on re-runs: a preset whose
    // base was renamed or dropped must not keep a link to the old parent row.
    private static void ResolveMachineBaseIds(Dictionary<string, MachineProfile> byName)
    {
        foreach (var profile in byName.Values)
        {
            if (profile.BaseProfileName is { } baseName && byName.TryGetValue(baseName, out var baseProfile))
            {
                profile.ResolveBaseProfile(baseProfile.Id);
            }
            else
            {
                profile.ClearBaseProfile();
            }
        }
    }

    private static void ResolveProcessBaseIds(Dictionary<string, ProcessProfile> byName)
    {
        foreach (var profile in byName.Values)
        {
            if (profile.BaseProfileName is { } baseName && byName.TryGetValue(baseName, out var baseProfile))
            {
                profile.ResolveBaseProfile(baseProfile.Id);
            }
            else
            {
                profile.ClearBaseProfile();
            }
        }
    }

    // OrcaSlicer's lookup rule: the preset's own vendor first, then the filament library.
    private static void ResolveFilamentBaseIds(Dictionary<string, FilamentProfile> byName, Dictionary<string, FilamentProfile>? library)
    {
        foreach (var profile in byName.Values)
        {
            FilamentProfile? baseProfile = null;
            var found = profile.BaseProfileName is { } baseName
                && (byName.TryGetValue(baseName, out baseProfile) || (library?.TryGetValue(baseName, out baseProfile) ?? false));
            if (found)
            {
                profile.ResolveBaseProfile(baseProfile!.Id);
            }
            else
            {
                profile.ClearBaseProfile();
            }
        }
    }

    private Dictionary<string, ParsedVendorPreset>? ReadFilamentLibrary(IReadOnlyList<string> vendorIndexFiles)
    {
        var indexFile = vendorIndexFiles.FirstOrDefault(file =>
            string.Equals(Path.GetFileNameWithoutExtension(file), OrcaFilamentLibraryName, StringComparison.OrdinalIgnoreCase));
        if (indexFile is null)
        {
            return null;
        }

        try
        {
            return indexReader.ReadPresets(indexFile, ProfileKind.Filament).Presets
                .GroupBy(preset => preset.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            LogSkippedVendorIndex(logger, indexFile, ex);
            return null;
        }
    }

    // The library presets a vendor's filaments inherit from, with their own ancestors — never the whole library:
    // its other presets are not ours to offer, and its names overlap the vendor's own. Library rows are hidden
    // (not selectable) and keyed under the library's name, whatever brand their chain declares.
    private static List<ParsedVendorPreset> LibraryClosure(
        IReadOnlyList<ParsedVendorPreset> vendorPresets, Dictionary<string, ParsedVendorPreset> library)
    {
        var ownNames = vendorPresets.Select(preset => preset.Name).ToHashSet(StringComparer.Ordinal);
        var closure = new Dictionary<string, ParsedVendorPreset>(StringComparer.Ordinal);
        foreach (var baseName in vendorPresets.Select(preset => preset.BaseProfileName).Where(name => name is not null && !ownNames.Contains(name)).Distinct())
        {
            var name = baseName;
            while (name is not null && library.TryGetValue(name, out var preset) && closure.TryAdd(name, preset))
            {
                name = preset.BaseProfileName;
            }
        }

        return closure.Values.Select(preset => preset with { VendorName = null, IsSelectable = false }).ToList();
    }

    // The profile is tied to a vendor, so ingesting it should make that brand available to pick from
    // when logging physical filament stock/spools too — scoped to filament brands only (machine/process
    // index-level vendor names like "Bambulab" aren't filament brands and don't share spelling with the
    // filament-side "Bambu Lab").
    private async Task EnsureVendorsExistAsync(IReadOnlySet<string> vendorNames, CancellationToken cancellationToken)
    {
        foreach (var vendorName in vendorNames)
        {
            var existing = await vendorRepository.GetByNameAsync(vendorName, cancellationToken);
            if (existing is null)
            {
                await vendorRepository.AddAsync(Vendor.Create(vendorName), cancellationToken);
            }
        }
    }

    private static bool SequenceEqualOrBothNull(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    // <root>/<engineSlug>/<version>/<platformSlug> (BundledSlicerScanner's own convention) —
    // bundleLeafPath is the platformSlug directory, so its parent is the version and its grandparent the
    // engine slug. The version directory name is the catalog version (SlicerRef.Version).
    private static (ProfileSourceFormat Format, string Version)? ResolveCatalog(string bundleLeafPath)
    {
        var versionDir = Directory.GetParent(bundleLeafPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var engineSlug = versionDir?.Parent?.Name;

        if (versionDir is null || string.IsNullOrEmpty(versionDir.Name) || engineSlug is null || !EngineSlugs.TryGetValue(engineSlug, out var engine))
        {
            return null;
        }

        return engine switch
        {
            SlicerEngine.OrcaSlicer => (ProfileSourceFormat.OrcaSlicer, versionDir.Name),
            SlicerEngine.BambuStudio => (ProfileSourceFormat.BambuStudio, versionDir.Name),
            _ => null,
        };
    }

    private static IngestionSummary EmptySummary() => new(0, 0, new(0, 0, 0), new(0, 0, 0), new(0, 0, 0));

    /// <summary>
    /// A System row's identity inside one catalog version — the same rule the unique indexes enforce:
    /// <c>(vendor, setting_id)</c> for a leaf, <c>(vendor, name)</c> for an abstract base without one.
    /// </summary>
    private readonly record struct IdentityKey(string Vendor, string? ExternalId, string? Name)
    {
        public static IdentityKey For(string vendor, string? externalId, string name) =>
            externalId is null ? new(vendor, null, name) : new(vendor, externalId, null);
    }

    /// <summary>One catalog's rows as loaded at the start of a run, plus the ids this run saw (for stale marking).</summary>
    private sealed class Catalog<TRow>(
        IReadOnlyList<TRow> loaded,
        Func<TRow, string?> vendorOf, Func<TRow, string?> externalIdOf, Func<TRow, string> nameOf, Func<TRow, Guid> idOf)
    {
        public Dictionary<IdentityKey, TRow> Index { get; } = BuildIndex(loaded, vendorOf, externalIdOf, nameOf);

        public HashSet<Guid> Seen { get; } = [];

        // research.md §1 — a previously-System row not re-seen this run is flagged stale, never deleted.
        public int MarkUnseenStale(Func<TRow, bool> isOffered, Action<TRow> markStale)
        {
            var stale = 0;
            foreach (var row in loaded)
            {
                if (isOffered(row) && !Seen.Contains(idOf(row)))
                {
                    markStale(row);
                    stale++;
                }
            }

            return stale;
        }

        private static Dictionary<IdentityKey, TRow> BuildIndex(
            IReadOnlyList<TRow> rows, Func<TRow, string?> vendorOf, Func<TRow, string?> externalIdOf, Func<TRow, string> nameOf)
        {
            var index = new Dictionary<IdentityKey, TRow>();
            foreach (var row in rows)
            {
                index.TryAdd(IdentityKey.For(vendorOf(row) ?? string.Empty, externalIdOf(row), nameOf(row)), row);
            }

            return index;
        }
    }

    private sealed class MutableCounts
    {
        public int Created;
        public int Updated;
        public int MarkedStale;

        public ProfileKindIngestionCounts ToImmutable() => new(Created, Updated, MarkedStale);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not resolve a bundled-slicer engine from leaf path '{Path}' — System Profile ingestion skipped for it.")]
    private static partial void LogUnresolvableEngine(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No 'resources/profiles' vendor index files were found under bundle leaf '{Path}' — nothing to ingest.")]
    private static partial void LogNoVendorIndexFilesFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unreadable/malformed vendor index '{Path}'.")]
    private static partial void LogSkippedVendorIndex(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipping vendor '{VendorName}' ({Path}) — no overlap with any supported machine model.")]
    private static partial void LogSkippedUnsupportedVendor(ILogger logger, string vendorName, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped unreadable/malformed preset file '{Path}' referenced by vendor '{VendorName}'.")]
    private static partial void LogSkippedPresetEntry(ILogger logger, string path, string vendorName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "setting_id '{ExternalId}' is shared by {Count} {Kind} presets of vendor '{VendorName}' — identifying them by name instead.")]
    private static partial void LogCollidingExternalId(ILogger logger, string externalId, int count, string kind, string vendorName);
}
