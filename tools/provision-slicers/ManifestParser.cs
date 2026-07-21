using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenFarm.Tools.ProvisionSlicers;

/// <summary><see cref="Manifest"/> is <c>null</c> exactly when <see cref="Errors"/> is non-empty.</summary>
public sealed record ManifestParseResult(SlicerManifest? Manifest, IReadOnlyList<string> Errors)
{
    public bool Success => Manifest is not null && Errors.Count == 0;
}

/// <summary>Parses/validates <c>slicers.manifest.json</c> against contracts/slicer-manifest.schema.json (research.md §8) — pure, deterministic, TDD.</summary>
public static class ManifestParser
{
    public static ManifestParseResult Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return new ManifestParseResult(null, [$"Invalid JSON: {ex.Message}"]);
        }

        if (root is not JsonObject obj)
        {
            return new ManifestParseResult(null, ["Manifest root must be a JSON object."]);
        }

        var errors = new List<string>();

        var schemaVersion = 1;
        if (obj.TryGetPropertyValue("schemaVersion", out var schemaVersionNode) && schemaVersionNode is not null)
        {
            schemaVersion = schemaVersionNode.GetValue<int>();
            if (schemaVersion != 1)
            {
                errors.Add($"Unsupported schemaVersion {schemaVersion}; expected 1.");
            }
        }

        if (!obj.TryGetPropertyValue("slicers", out var slicersNode) || slicersNode is not JsonArray slicersArray)
        {
            errors.Add("Manifest must have a 'slicers' array.");
            return new ManifestParseResult(null, errors);
        }

        if (slicersArray.Count == 0)
        {
            errors.Add("Manifest 'slicers' array must not be empty.");
        }

        var entries = new List<SlicerManifestEntry>();
        for (var i = 0; i < slicersArray.Count; i++)
        {
            if (slicersArray[i] is not JsonObject entryObj)
            {
                errors.Add($"slicers[{i}] must be a JSON object.");
                continue;
            }

            var entryErrors = ParseEntry(i, entryObj, out var entry);
            if (entryErrors.Count > 0)
            {
                errors.AddRange(entryErrors);
            }
            else
            {
                entries.Add(entry!);
            }
        }

        return errors.Count > 0
            ? new ManifestParseResult(null, errors)
            : new ManifestParseResult(new SlicerManifest(schemaVersion, entries), []);
    }

    private static List<string> ParseEntry(int index, JsonObject entryObj, out SlicerManifestEntry? entry)
    {
        var errors = new List<string>();
        entry = null;

        var engineRaw = entryObj["engine"]?.GetValue<string>();
        Engine engine = default;
        if (string.IsNullOrWhiteSpace(engineRaw))
        {
            errors.Add($"slicers[{index}].engine is required.");
        }
        else if (!Enum.TryParse(engineRaw, out engine))
        {
            errors.Add($"slicers[{index}].engine '{engineRaw}' is not a known engine (OrcaSlicer, BambuStudio, SuperSlicer).");
        }

        var version = entryObj["version"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(version))
        {
            errors.Add($"slicers[{index}].version is required.");
        }

        var platforms = new List<Platform>();
        if (entryObj["platforms"] is JsonArray platformsArray)
        {
            if (platformsArray.Count == 0)
            {
                errors.Add($"slicers[{index}].platforms must not be empty.");
            }

            foreach (var platformNode in platformsArray)
            {
                var platformRaw = platformNode?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(platformRaw) && Enum.TryParse<Platform>(platformRaw, out var platform))
                {
                    platforms.Add(platform);
                }
                else
                {
                    errors.Add($"slicers[{index}].platforms contains an unknown platform '{platformRaw}'.");
                }
            }
        }
        else
        {
            errors.Add($"slicers[{index}].platforms is required.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var notes = entryObj["notes"]?.GetValue<string>();
        entry = new SlicerManifestEntry(engine, version!, platforms, notes);
        return errors;
    }
}
