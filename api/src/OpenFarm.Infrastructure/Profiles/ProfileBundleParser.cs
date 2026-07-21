using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Profiles;

// ZipArchive-based; classifies presets by machine/filament subfolder rather than depending on the
// unverifiable bundle_structure.json manifest shape (research.md §1), and tolerates missing keys
// rather than failing the whole import (research.md §2).
public sealed class ProfileBundleParser : IProfileBundleParser
{
    public async Task<ParsedProfileBundle> ParseAsync(
        Stream bundleStream, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(bundleStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        // ZipArchive throws different exception types depending on exactly how a stream fails to
        // look like a zip (too short to contain an End Of Central Directory record, garbled
        // central directory, etc.) — all of them mean the same thing to an operator: this isn't a
        // usable bundle (FR-007).
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IOException or EndOfStreamException)
        {
            throw new UnsupportedProfileBundleFormatException(
                "The uploaded file could not be read as a slicer profile bundle (not a valid zip archive).", ex);
        }

        var machinePresets = new List<ParsedMachinePreset>();
        var filamentPresets = new List<ParsedFilamentPreset>();
        var processPresets = new List<ParsedProcessPreset>();

        using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var kind = ClassifyBySubfolder(entry.FullName);
                if (kind is null)
                {
                    continue;
                }

                string content;
                await using (var entryStream = entry.Open())
                using (var reader = new StreamReader(entryStream))
                {
                    content = await reader.ReadToEndAsync(cancellationToken);
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(content);
                }
                catch (JsonException)
                {
                    // One malformed preset inside an otherwise-good bundle is excluded, not a
                    // whole-bundle rejection (data-model.md's Validation rules / FR-007 scoping).
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    var name = TryGetScalarOrFirstArrayElement(root, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        // A preset with no name is excluded from the preview list entirely
                        // (data-model.md's Validation rules), not a bundle-wide failure.
                        continue;
                    }

                    var baseProfileName = TryGetScalarOrFirstArrayElement(root, "inherits");
                    if (string.IsNullOrWhiteSpace(baseProfileName))
                    {
                        baseProfileName = null;
                    }

                    switch (kind)
                    {
                        case PresetKind.Machine:
                            machinePresets.Add(new ParsedMachinePreset(name, sourceFormat, baseProfileName, content));
                            break;
                        case PresetKind.Filament:
                            filamentPresets.Add(new ParsedFilamentPreset(name, sourceFormat, baseProfileName, content));
                            break;
                        case PresetKind.Process:
                            processPresets.Add(new ParsedProcessPreset(name, sourceFormat, baseProfileName, content));
                            break;
                    }
                }
            }
        }

        return new ParsedProfileBundle(machinePresets, filamentPresets, processPresets);
    }

    public MachineProfileDisplayFields ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat)
    {
        using var document = JsonDocument.Parse(rawDataJson);
        var root = document.RootElement;

        double? bedSizeX = null;
        double? bedSizeY = null;
        if (TryGetStringArray(root, "printable_area", out var corners))
        {
            foreach (var corner in corners)
            {
                var parts = corner.Split('x', StringSplitOptions.TrimEntries);
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    bedSizeX = bedSizeX is null ? x : Math.Max(bedSizeX.Value, x);
                    bedSizeY = bedSizeY is null ? y : Math.Max(bedSizeY.Value, y);
                }
            }
        }

        return new MachineProfileDisplayFields(
            NozzleSizeMm: TryGetDouble(root, "nozzle_diameter"),
            BedSizeXMm: bedSizeX,
            BedSizeYMm: bedSizeY,
            MaxHeightMm: TryGetDouble(root, "printable_height"),
            HasEnclosure: TryGetBool(root, "support_chamber_temp_control"),
            SupportedMaterials: TryGetStringArray(root, "supported_filament_types", out var materials) ? materials : []);
    }

    public FilamentProfileDisplayFields ExtractFilamentDisplayFields(
        string rawDataJson, ProfileSourceFormat sourceFormat, string name, bool hasBaseProfileName)
    {
        using var document = JsonDocument.Parse(rawDataJson);
        var root = document.RootElement;

        // Tolerates the real, documented OrcaSlicer key-spelling bug (missing the trailing "er"),
        // research.md §11 / OrcaSlicer issue #12193.
        var hasCompatiblePrinters =
            TryGetStringArray(root, "compatible_printers", out var compatiblePrinterNames)
            || TryGetStringArray(root, "compatible_prints", out compatiblePrinterNames);

        if ((!hasCompatiblePrinters || compatiblePrinterNames.Count == 0) && !hasBaseProfileName)
        {
            // Root-preset name-suffix fallback ("<filament> @ <printer>"), research.md §11 —
            // only applies when the preset declares neither a base nor an explicit list.
            var atIndex = name.LastIndexOf('@');
            if (atIndex >= 0 && atIndex + 1 < name.Length)
            {
                var inferredPrinterName = name[(atIndex + 1)..].Trim();
                if (inferredPrinterName.Length > 0)
                {
                    compatiblePrinterNames = [inferredPrinterName];
                }
            }
        }

        return new FilamentProfileDisplayFields(
            Material: TryGetScalarOrFirstArrayElement(root, "filament_type"),
            Brand: TryGetScalarOrFirstArrayElement(root, "filament_vendor"),
            ColorHex: TryGetScalarOrFirstArrayElement(root, "default_filament_colour"),
            NozzleSizeMm: TryGetDouble(root, "nozzle_diameter"),
            NozzleTempC: TryGetDouble(root, "nozzle_temperature"),
            BedTempC: TryGetDouble(root, "hot_plate_temp"),
            MaxVolumetricSpeedMm3s: TryGetDouble(root, "filament_max_volumetric_speed"),
            FanPercent: TryGetInt(root, "fan_max_speed"),
            CompatiblePrinterNames: compatiblePrinterNames);
    }

    public ProcessProfileDisplayFields ExtractProcessDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat)
    {
        using var document = JsonDocument.Parse(rawDataJson);
        var root = document.RootElement;

        // Same tolerant key-spelling as ExtractFilamentDisplayFields (research.md §11 / OrcaSlicer
        // issue #12193) — a UI hint only, never used to auto-apply a machine/process pairing.
        var hasCompatiblePrinters =
            TryGetStringArray(root, "compatible_printers", out var compatiblePrinterNames)
            || TryGetStringArray(root, "compatible_prints", out compatiblePrinterNames);

        return new ProcessProfileDisplayFields(
            LayerHeightMm: TryGetDouble(root, "layer_height"),
            WallLoops: TryGetInt(root, "wall_loops"),
            CompatiblePrinterNames: hasCompatiblePrinters ? compatiblePrinterNames : []);
    }

    private enum PresetKind
    {
        Machine,
        Filament,
        Process,
    }

    private static PresetKind? ClassifyBySubfolder(string entryFullName)
    {
        var normalized = entryFullName.Replace('\\', '/');
        var firstSegmentEnd = normalized.IndexOf('/');
        if (firstSegmentEnd < 0)
        {
            return null;
        }

        var firstSegment = normalized[..firstSegmentEnd];
        // Real OrcaSlicer "Export current printer" bundles (verified against an actual .orca_printer
        // export) use a "printer/" subfolder for the machine preset, not "machine/" as the multi-preset
        // "Export Preset Bundle" shape does — both are accepted so neither export path drops presets.
        if (firstSegment.Equals("machine", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("printer", StringComparison.OrdinalIgnoreCase))
        {
            return PresetKind.Machine;
        }

        if (firstSegment.Equals("filament", StringComparison.OrdinalIgnoreCase))
        {
            return PresetKind.Filament;
        }

        if (firstSegment.Equals("process", StringComparison.OrdinalIgnoreCase))
        {
            return PresetKind.Process;
        }

        // Anything else, including bundle_structure.json, is out of scope.
        return null;
    }

    private static string? TryGetScalarOrFirstArrayElement(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array when value.GetArrayLength() > 0 => value[0].ValueKind switch
            {
                JsonValueKind.String => value[0].GetString(),
                JsonValueKind.Number => value[0].GetRawText(),
                _ => null,
            },
            _ => null,
        };
    }

    private static bool TryGetStringArray(JsonElement root, string propertyName, out IReadOnlyList<string> values)
    {
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            values = value.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
            return values.Count > 0;
        }

        values = [];
        return false;
    }

    private static double? TryGetDouble(JsonElement root, string propertyName)
    {
        var raw = TryGetScalarOrFirstArrayElement(root, propertyName);
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        var raw = TryGetScalarOrFirstArrayElement(root, propertyName);
        return raw is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDouble)
                ? (int)Math.Round(asDouble)
                : null;
    }

    private static bool? TryGetBool(JsonElement root, string propertyName)
    {
        var raw = TryGetScalarOrFirstArrayElement(root, propertyName);
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
    }
}
