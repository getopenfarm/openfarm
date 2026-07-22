using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Profiles;
using static OpenFarm.Infrastructure.Profiles.JsonPresetFieldHelpers;

namespace OpenFarm.Infrastructure.Profiles;

/// <summary>One vendor's own top-level identity plus its supported machine model names (research.md §0), used for ingestion scoping (FR-001).</summary>
public sealed record VendorIndexSummary(string VendorName, IReadOnlyList<string> MachineModelNames);

/// <summary>
/// One fetched preset of a given <see cref="ProfileKind"/>, already filtered through the research.md
/// §0a type-match guard: <see cref="IsSelectable"/> requires both the vendor's own <c>instantiation</c>
/// flag and the fetched file's own declared <c>type</c> matching the list it came from — list
/// membership alone (e.g. Bambu Studio's <c>machine_list</c>) is not a reliable "this is a preset of
/// this kind" signal.
/// </summary>
public sealed record ParsedVendorPreset(
    string Name,
    string RawJson,
    string? BaseProfileName,
    string? ExternalId,
    bool IsSelectable,
    IReadOnlyList<string>? CompatibleMachineNames,
    string? VendorMachineModelName,
    IReadOnlyList<string>? IncludedTemplateNames,
    string? MaterialType,
    string? VendorName);

public sealed record VendorPresetReadResult(IReadOnlyList<ParsedVendorPreset> Presets, IReadOnlyList<string> SkippedEntries);

/// <summary>
/// Parses one vendor's <c>resources/profiles/&lt;Vendor&gt;.json</c> index plus every preset file it
/// references for a given <see cref="ProfileKind"/> (research.md §0/§0a). A malformed or missing
/// referenced preset file is skipped, never aborting the rest of the vendor's presets (contracts/
/// system-profile-ingestion.md's failure-handling contract) — the ingestion service decides what a
/// non-empty <see cref="VendorPresetReadResult.SkippedEntries"/> means for its own summary reporting.
/// </summary>
public sealed partial class VendorProfileIndexReader(ILogger<VendorProfileIndexReader> logger)
{
    private const int MaxChainDepth = 32;

    private static readonly Dictionary<ProfileKind, string> ListKeys = new()
    {
        [ProfileKind.Machine] = "machine_list",
        [ProfileKind.Process] = "process_list",
        [ProfileKind.Filament] = "filament_list",
    };

    private static readonly Dictionary<ProfileKind, string> ExpectedTypeStrings = new()
    {
        [ProfileKind.Machine] = "machine",
        [ProfileKind.Process] = "process",
        [ProfileKind.Filament] = "filament",
    };

    /// <summary>The vendor index's own <c>name</c> and <c>machine_model_list</c> — read-only-for-scoping input (contracts/system-profile-ingestion.md).</summary>
    public static VendorIndexSummary ReadSummary(string vendorIndexFilePath)
    {
        using var stream = File.OpenRead(vendorIndexFilePath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var vendorName = TryGetScalarOrFirstArrayElement(root, "name")
            ?? throw new InvalidDataException($"Vendor index '{vendorIndexFilePath}' has no top-level 'name'.");

        var modelNames = new List<string>();
        if (root.TryGetProperty("machine_model_list", out var modelList) && modelList.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in modelList.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("name", out var nameElement)
                    && nameElement.ValueKind == JsonValueKind.String
                    && nameElement.GetString() is { Length: > 0 } modelName)
                {
                    modelNames.Add(modelName);
                }
            }
        }

        return new VendorIndexSummary(vendorName, modelNames);
    }

    /// <summary>
    /// Fetches every preset of <paramref name="kind"/> the vendor index references, resolving each
    /// filament's <see cref="ParsedVendorPreset.MaterialType"/> by walking its own ancestor chain
    /// within this same fetched batch (research.md §0 point 5 — every ancestor a leaf preset declares
    /// is itself present in the same vendor file, so no external lookup is needed at read time).
    /// </summary>
    public VendorPresetReadResult ReadPresets(string vendorIndexFilePath, ProfileKind kind)
    {
        using var indexStream = File.OpenRead(vendorIndexFilePath);
        using var indexDocument = JsonDocument.Parse(indexStream);
        var indexRoot = indexDocument.RootElement;

        var listKey = ListKeys[kind];
        var expectedType = ExpectedTypeStrings[kind];
        var baseDirectory = Path.GetDirectoryName(vendorIndexFilePath) ?? string.Empty;
        // A real bundle (confirmed against a real Bambu Studio install, not just this repo's
        // simplified/flattened test fixtures) nests every preset one level deeper than the vendor
        // index file itself — resources/profiles/BBL.json indexes resources/profiles/BBL/machine/…,
        // not resources/profiles/machine/… — in a subdirectory named after the index file's own stem.
        var vendorSubdirectory = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(vendorIndexFilePath));

        var presets = new List<ParsedVendorPreset>();
        var skipped = new List<string>();

        if (!indexRoot.TryGetProperty(listKey, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return new VendorPresetReadResult(presets, skipped);
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("sub_path", out var subPathElement)
                || subPathElement.ValueKind != JsonValueKind.String
                || subPathElement.GetString() is not { Length: > 0 } subPath)
            {
                continue;
            }

            var relativePath = subPath.Replace('/', Path.DirectorySeparatorChar);
            var flatPath = Path.Combine(baseDirectory, relativePath);
            var nestedPath = Path.Combine(vendorSubdirectory, relativePath);
            var presetPath = File.Exists(nestedPath) ? nestedPath : flatPath;

            string rawJson;
            JsonDocument presetDocument;
            try
            {
                rawJson = File.ReadAllText(presetPath);
                presetDocument = JsonDocument.Parse(rawJson);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LogSkippedPresetFile(logger, presetPath, ex);
                skipped.Add(presetPath);
                continue;
            }

            using (presetDocument)
            {
                var root = presetDocument.RootElement;
                var name = TryGetScalarOrFirstArrayElement(root, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    LogSkippedUnnamedPreset(logger, presetPath);
                    skipped.Add(presetPath);
                    continue;
                }

                var declaredType = TryGetScalarOrFirstArrayElement(root, "type");
                var isInstantiationTrue = TryGetScalarOrFirstArrayElement(root, "instantiation") == "true";
                var isTypeMatch = string.Equals(declaredType, expectedType, StringComparison.Ordinal);

                var baseProfileName = TryGetScalarOrFirstArrayElement(root, "inherits");
                var externalId = TryGetScalarOrFirstArrayElement(root, "setting_id");
                var compatibleMachineNames = TryGetStringArray(root, "compatible_printers", out var compatibleList) ? compatibleList : null;
                var vendorMachineModelName = kind == ProfileKind.Machine ? TryGetScalarOrFirstArrayElement(root, "printer_model") : null;
                var includedTemplateNames = TryGetStringArray(root, "include", out var includeList) ? includeList : null;

                var isSelectable = isTypeMatch && isInstantiationTrue;

                presets.Add(new ParsedVendorPreset(
                    name,
                    rawJson,
                    string.IsNullOrWhiteSpace(baseProfileName) ? null : baseProfileName,
                    string.IsNullOrWhiteSpace(externalId) ? null : externalId,
                    isSelectable,
                    compatibleMachineNames,
                    string.IsNullOrWhiteSpace(vendorMachineModelName) ? null : vendorMachineModelName,
                    includedTemplateNames,
                    null,
                    null));
            }
        }

        if (kind == ProfileKind.Filament)
        {
            presets = ResolveFilamentDerivedFields(presets);
        }

        return new VendorPresetReadResult(presets, skipped);
    }

    // research.md §0 point 5: filament_type is set once on the material-family root and inherited
    // down, never re-declared per leaf — resolved here the same way ProfileRawDataMerger already
    // flattens a persisted ancestor chain, just walked by Name within this in-memory batch instead of
    // by BaseProfileId against the database (nothing has been persisted yet at read time). filament_vendor
    // (OrcaSlicer's own filament-edit "Vendor" field) follows the identical shape — declared once on the
    // material-family root (e.g. "Generic") and overridden further down for branded presets (e.g. "Bambu
    // Lab") — so it's resolved off the same merged ancestor document rather than the vendor index's own
    // top-level name, which real bundles mix many actual brands under (a single BBL.json filament index
    // covers Bambu-branded presets alongside Generic/eSUN/SUNLU/etc.).
    private static List<ParsedVendorPreset> ResolveFilamentDerivedFields(List<ParsedVendorPreset> presets)
    {
        var byName = presets
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var resolved = new List<ParsedVendorPreset>(presets.Count);
        foreach (var preset in presets)
        {
            var ancestorsOldestFirst = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var currentName = (string?)preset.Name;
            var depth = 0;

            while (currentName is not null && depth++ < MaxChainDepth && visited.Add(currentName) && byName.TryGetValue(currentName, out var node))
            {
                ancestorsOldestFirst.Insert(0, node.RawJson);
                currentName = node.BaseProfileName;
            }

            var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
            using var mergedDocument = JsonDocument.Parse(merged);
            var materialType = TryGetScalarOrFirstArrayElement(mergedDocument.RootElement, "filament_type");
            var vendorName = TryGetScalarOrFirstArrayElement(mergedDocument.RootElement, "filament_vendor");

            resolved.Add(preset with { MaterialType = materialType, VendorName = vendorName });
        }

        return resolved;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unreadable/malformed vendor preset file '{Path}' referenced by a vendor index.")]
    private static partial void LogSkippedPresetFile(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unnamed vendor preset file '{Path}'.")]
    private static partial void LogSkippedUnnamedPreset(ILogger logger, string path);
}
