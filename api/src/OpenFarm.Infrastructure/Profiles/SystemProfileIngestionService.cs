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
/// </summary>
public sealed partial class SystemProfileIngestionService(
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IVendorRepository vendorRepository,
    VendorProfileIndexReader indexReader,
    ILogger<SystemProfileIngestionService> logger) : ISystemProfileIngestionService
{
    private static readonly Dictionary<string, SlicerEngine> EngineSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orcaslicer"] = SlicerEngine.OrcaSlicer,
        ["bambustudio"] = SlicerEngine.BambuStudio,
    };

    public async Task<IngestionSummary> IngestAsync(string bundleLeafPath, CancellationToken cancellationToken)
    {
        var sourceFormat = ResolveSourceFormat(bundleLeafPath);
        if (sourceFormat is null)
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

        var seenMachineIds = new HashSet<Guid>();
        var seenProcessIds = new HashSet<Guid>();
        var seenFilamentIds = new HashSet<Guid>();
        var filamentVendorNamesSeen = new HashSet<string>(StringComparer.Ordinal);

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
            var filamentResult = indexReader.ReadPresets(vendorIndexFile, ProfileKind.Filament);

            foreach (var skipped in machineResult.SkippedEntries.Concat(processResult.SkippedEntries).Concat(filamentResult.SkippedEntries))
            {
                LogSkippedPresetEntry(logger, skipped, summary.VendorName);
            }

            var machineByName = await UpsertMachineAsync(summary.VendorName, sourceFormat.Value, machineResult.Presets, machineCounts, seenMachineIds, cancellationToken);
            var processByName = await UpsertProcessAsync(summary.VendorName, sourceFormat.Value, processResult.Presets, processCounts, seenProcessIds, cancellationToken);
            var filamentByName = await UpsertFilamentAsync(summary.VendorName, sourceFormat.Value, filamentResult.Presets, filamentCounts, seenFilamentIds, cancellationToken);
            foreach (var filamentProfile in filamentByName.Values)
            {
                if (filamentProfile.VendorName is { } filamentVendorName)
                {
                    filamentVendorNamesSeen.Add(filamentVendorName);
                }
            }

            ResolveMachineBaseIds(machineByName);
            ResolveProcessBaseIds(processByName);
            ResolveFilamentBaseIds(filamentByName);

            await machineProfileRepository.SaveChangesAsync(cancellationToken);
            await processProfileRepository.SaveChangesAsync(cancellationToken);
            await filamentProfileRepository.SaveChangesAsync(cancellationToken);
        }

        await EnsureVendorsExistAsync(filamentVendorNamesSeen, cancellationToken);

        machineCounts.MarkedStale += await MarkStaleAsync(machineProfileRepository, seenMachineIds, sourceFormat.Value, cancellationToken);
        processCounts.MarkedStale += await MarkStaleAsync(processProfileRepository, seenProcessIds, sourceFormat.Value, cancellationToken);
        filamentCounts.MarkedStale += await MarkStaleAsync(filamentProfileRepository, seenFilamentIds, sourceFormat.Value, cancellationToken);

        return new IngestionSummary(
            vendorsScanned,
            vendorsSkipped,
            machineCounts.ToImmutable(),
            processCounts.ToImmutable(),
            filamentCounts.ToImmutable());
    }

    private async Task<Dictionary<string, MachineProfile>> UpsertMachineAsync(
        string vendorName, ProfileSourceFormat sourceFormat,
        IReadOnlyList<ParsedVendorPreset> presets, MutableCounts counts, HashSet<Guid> seenIds, CancellationToken cancellationToken)
    {
        var byName = new Dictionary<string, MachineProfile>(StringComparer.Ordinal);

        foreach (var preset in presets)
        {
            var existing = preset.ExternalId is not null
                ? await machineProfileRepository.FindByExternalIdAsync(sourceFormat, vendorName, preset.ExternalId, cancellationToken)
                : await machineProfileRepository.FindSystemByNameAsync(sourceFormat, vendorName, preset.Name, cancellationToken);

            if (existing is not null)
            {
                var changed = existing.Name != preset.Name
                    || existing.RawDataJson != preset.RawJson
                    || existing.IsSelectable != preset.IsSelectable
                    || existing.VendorMachineModelName != preset.VendorMachineModelName
                    || !SequenceEqualOrBothNull(existing.IncludedTemplateNames, preset.IncludedTemplateNames);

                existing.RefreshFromSystemSource(preset.Name, preset.RawJson, preset.IsSelectable, preset.VendorMachineModelName, preset.IncludedTemplateNames);
                if (changed)
                {
                    counts.Updated++;
                }

                seenIds.Add(existing.Id);
                byName[preset.Name] = existing;
                continue;
            }

            var created = MachineProfile.CreateSystemProfile(
                preset.Name, sourceFormat, preset.BaseProfileName, preset.RawJson,
                vendorName, preset.ExternalId, preset.IsSelectable, preset.VendorMachineModelName, preset.IncludedTemplateNames);
            await machineProfileRepository.AddAsync(created, cancellationToken);
            counts.Created++;
            seenIds.Add(created.Id);
            byName[preset.Name] = created;
        }

        return byName;
    }

    private async Task<Dictionary<string, ProcessProfile>> UpsertProcessAsync(
        string vendorName, ProfileSourceFormat sourceFormat,
        IReadOnlyList<ParsedVendorPreset> presets, MutableCounts counts, HashSet<Guid> seenIds, CancellationToken cancellationToken)
    {
        var byName = new Dictionary<string, ProcessProfile>(StringComparer.Ordinal);

        foreach (var preset in presets)
        {
            var existing = preset.ExternalId is not null
                ? await processProfileRepository.FindByExternalIdAsync(sourceFormat, vendorName, preset.ExternalId, cancellationToken)
                : await processProfileRepository.FindSystemByNameAsync(sourceFormat, vendorName, preset.Name, cancellationToken);

            if (existing is not null)
            {
                var changed = existing.Name != preset.Name
                    || existing.RawDataJson != preset.RawJson
                    || existing.IsSelectable != preset.IsSelectable
                    || !SequenceEqualOrBothNull(existing.CompatibleMachineNames, preset.CompatibleMachineNames);

                existing.RefreshFromSystemSource(preset.Name, preset.RawJson, preset.IsSelectable, preset.CompatibleMachineNames);
                if (changed)
                {
                    counts.Updated++;
                }

                seenIds.Add(existing.Id);
                byName[preset.Name] = existing;
                continue;
            }

            var created = ProcessProfile.CreateSystemProfile(
                preset.Name, sourceFormat, preset.BaseProfileName, preset.RawJson,
                vendorName, preset.ExternalId, preset.IsSelectable, preset.CompatibleMachineNames);
            await processProfileRepository.AddAsync(created, cancellationToken);
            counts.Created++;
            seenIds.Add(created.Id);
            byName[preset.Name] = created;
        }

        return byName;
    }

    private async Task<Dictionary<string, FilamentProfile>> UpsertFilamentAsync(
        string vendorName, ProfileSourceFormat sourceFormat,
        IReadOnlyList<ParsedVendorPreset> presets, MutableCounts counts, HashSet<Guid> seenIds, CancellationToken cancellationToken)
    {
        var byName = new Dictionary<string, FilamentProfile>(StringComparer.Ordinal);

        foreach (var preset in presets)
        {
            // Falls back to the vendor index's own name only if this preset's ancestor chain never
            // declares filament_vendor — the resolved per-preset brand (research.md §0, VendorProfileIndexReader)
            // is what a real bundle actually means, since one filament index file bundles many brands.
            var resolvedVendorName = preset.VendorName ?? vendorName;

            var existing = preset.ExternalId is not null
                ? await filamentProfileRepository.FindByExternalIdAsync(sourceFormat, resolvedVendorName, preset.ExternalId, cancellationToken)
                : await filamentProfileRepository.FindSystemByNameAsync(sourceFormat, resolvedVendorName, preset.Name, cancellationToken);

            if (existing is not null)
            {
                var changed = existing.Name != preset.Name
                    || existing.RawDataJson != preset.RawJson
                    || existing.IsSelectable != preset.IsSelectable
                    || existing.MaterialType != preset.MaterialType
                    || !SequenceEqualOrBothNull(existing.CompatibleMachineNames, preset.CompatibleMachineNames)
                    || !SequenceEqualOrBothNull(existing.IncludedTemplateNames, preset.IncludedTemplateNames);

                existing.RefreshFromSystemSource(
                    preset.Name, preset.RawJson, preset.IsSelectable, preset.CompatibleMachineNames, preset.MaterialType, preset.IncludedTemplateNames);
                if (changed)
                {
                    counts.Updated++;
                }

                seenIds.Add(existing.Id);
                byName[preset.Name] = existing;
                continue;
            }

            var created = FilamentProfile.CreateSystemProfile(
                preset.Name, sourceFormat, preset.BaseProfileName, preset.RawJson,
                resolvedVendorName, preset.ExternalId, preset.IsSelectable, preset.CompatibleMachineNames, preset.MaterialType, preset.IncludedTemplateNames);
            await filamentProfileRepository.AddAsync(created, cancellationToken);
            counts.Created++;
            seenIds.Add(created.Id);
            byName[preset.Name] = created;
        }

        return byName;
    }

    // Pass 2 — resolves BaseProfileId purely within this vendor's own just-upserted batch: research.md
    // §0 confirms every ancestor a leaf declares via "inherits" is itself present in the same vendor
    // file, so no cross-vendor or cross-run lookup is ever needed.
    private static void ResolveMachineBaseIds(Dictionary<string, MachineProfile> byName)
    {
        foreach (var profile in byName.Values)
        {
            if (profile.BaseProfileName is { } baseName && byName.TryGetValue(baseName, out var baseProfile))
            {
                profile.ResolveBaseProfile(baseProfile.Id);
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
        }
    }

    private static void ResolveFilamentBaseIds(Dictionary<string, FilamentProfile> byName)
    {
        foreach (var profile in byName.Values)
        {
            if (profile.BaseProfileName is { } baseName && byName.TryGetValue(baseName, out var baseProfile))
            {
                profile.ResolveBaseProfile(baseProfile.Id);
            }
        }
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

    // research.md §1 — a previously-System row not re-seen this run is flagged stale, never deleted.
    // One overload per repository type rather than a shared generic constraint (each entity's own
    // Origin/IsCurrentlyOffered/MarkStale members aren't unified by a common interface, and ListAsync's
    // unbounded read is fine here — a one-shot batch job, not a per-request path, research.md §6).
    private static async Task<int> MarkStaleAsync(IMachineProfileRepository repository, HashSet<Guid> seenIds, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken)
    {
        var all = await repository.ListAsync(cancellationToken);
        var staleCount = 0;
        foreach (var row in all)
        {
            if (row.SourceFormat == sourceFormat && row.Origin == ProfileOrigin.System && row.IsCurrentlyOffered && !seenIds.Contains(row.Id))
            {
                (await repository.GetByIdAsync(row.Id, cancellationToken))!.MarkStale();
                staleCount++;
            }
        }

        if (staleCount > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return staleCount;
    }

    private static async Task<int> MarkStaleAsync(IProcessProfileRepository repository, HashSet<Guid> seenIds, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken)
    {
        var all = await repository.ListAsync(cancellationToken);
        var staleCount = 0;
        foreach (var row in all)
        {
            if (row.SourceFormat == sourceFormat && row.Origin == ProfileOrigin.System && row.IsCurrentlyOffered && !seenIds.Contains(row.Id))
            {
                (await repository.GetByIdAsync(row.Id, cancellationToken))!.MarkStale();
                staleCount++;
            }
        }

        if (staleCount > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return staleCount;
    }

    private static async Task<int> MarkStaleAsync(IFilamentProfileRepository repository, HashSet<Guid> seenIds, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken)
    {
        var all = await repository.ListAsync(cancellationToken);
        var staleCount = 0;
        foreach (var row in all)
        {
            if (row.SourceFormat == sourceFormat && row.Origin == ProfileOrigin.System && row.IsCurrentlyOffered && !seenIds.Contains(row.Id))
            {
                (await repository.GetByIdAsync(row.Id, cancellationToken))!.MarkStale();
                staleCount++;
            }
        }

        if (staleCount > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return staleCount;
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

    private static ProfileSourceFormat? ResolveSourceFormat(string bundleLeafPath)
    {
        // <root>/<engineSlug>/<version>/<platformSlug> (BundledSlicerScanner's own convention) —
        // bundleLeafPath is the platformSlug directory, so its grandparent is the engine slug.
        var versionDir = Directory.GetParent(bundleLeafPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var engineDir = versionDir?.Parent;
        var engineSlug = engineDir?.Name;

        if (engineSlug is null || !EngineSlugs.TryGetValue(engineSlug, out var engine))
        {
            return null;
        }

        return engine switch
        {
            SlicerEngine.OrcaSlicer => ProfileSourceFormat.OrcaSlicer,
            SlicerEngine.BambuStudio => ProfileSourceFormat.BambuStudio,
            _ => null,
        };
    }

    private static IngestionSummary EmptySummary() => new(0, 0, new(0, 0, 0), new(0, 0, 0), new(0, 0, 0));

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
}
