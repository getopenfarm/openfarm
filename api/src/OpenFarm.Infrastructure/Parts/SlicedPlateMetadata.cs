using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using OpenFarm.Application.Parts;

namespace OpenFarm.Infrastructure.Parts;

internal sealed record SlicedPlateMetadata(decimal? Nozzle, int? Minutes, decimal? Weight,
    IReadOnlyList<DetectedFilamentSlot> Slots, bool MaterialsComplete, bool NozzleConflict = false,
    string? MachineModelId = null, IReadOnlyList<int>? UsedFilamentIndices = null)
{
    internal static SlicedPlateMetadata Read(ZipArchive archive, int plateNumber, List<string> warnings)
    {
        decimal? nozzle = null;
        int? minutes = null;
        decimal? weight = null;
        string? machine = null;
        List<DetectedFilamentSlot> slots = [];
        var materialsComplete = true;
        var nozzleConflict = false;
        IReadOnlyList<int>? usedIndices = null;
        var sliceInfo = Find(archive, "Metadata/slice_info.config");
        if (sliceInfo is not null)
        {
            try
            {
                using var input = sliceInfo.Open();
                var xml = XDocument.Load(input);
                var plate = xml.Descendants("plate").FirstOrDefault(p => p.Elements("metadata")
                    .Any(m => (string?)m.Attribute("key") == "index" && (string?)m.Attribute("value") == plateNumber.ToString(CultureInfo.InvariantCulture)));
                if (plate is not null)
                {
                    string? Value(string key) => plate.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == key)?.Attribute("value")?.Value;
                    machine = Value("printer_model_id");
                    var nozzles = new List<decimal>();
                    if (Value("nozzle_diameters") is { } raw)
                    {
                        foreach (var n in raw.Split([',', ';'], StringSplitOptions.TrimEntries))
                            if (Number(n) is > 0 and var parsed) nozzles.Add(decimal.Round(parsed, 4));
                            else nozzleConflict = true;
                    }
                    foreach (var node in plate.Elements("nozzle"))
                        if (Number((string?)node.Attribute("nozzle_diameter")) is > 0 and var n) nozzles.Add(decimal.Round(n, 4));
                    nozzleConflict |= nozzles.Distinct().Count() > 1;
                    nozzle = !nozzleConflict && nozzles.Count > 0 ? nozzles[0] : null;
                    if (Number(Value("prediction")) is >= 0 and var seconds && seconds <= int.MaxValue * 60m)
                        minutes = seconds > 0 && seconds < 60 ? 1 : (int)(seconds / 60);
                    weight = Number(Value("weight"));
                    foreach (var filament in plate.Elements("filament"))
                    {
                        var explicitlyUnused = (string?)filament.Attribute("used_for_object") == "false"
                            && (string?)filament.Attribute("used_for_support") == "false";
                        var hasUsage = filament.Attribute("used_g") is not null || filament.Attribute("used_m") is not null;
                        var explicitlyUsed = (string?)filament.Attribute("used_for_object") == "true"
                            || (string?)filament.Attribute("used_for_support") == "true";
                        if ((explicitlyUnused || hasUsage && !explicitlyUsed)
                            && Number((string?)filament.Attribute("used_g")) is not > 0
                            && Number((string?)filament.Attribute("used_m")) is not > 0) continue;
                        var type = (string?)filament.Attribute("type");
                        if (string.IsNullOrWhiteSpace(type)) { materialsComplete = false; continue; }
                        slots.Add(new(type, NormalizeColor((string?)filament.Attribute("color")), null));
                        if (Number((string?)filament.Attribute("nozzle_diameter")) is > 0 and var filamentNozzle)
                            nozzles.Add(decimal.Round(filamentNozzle, 4));
                    }
                    nozzleConflict |= nozzles.Distinct().Count() > 1;
                    nozzle = !nozzleConflict && nozzles.Count > 0 ? nozzles[0] : null;
                }
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
            { warnings.Add($"Plate {plateNumber}: slice metadata could not be read; using other embedded sources."); }
        }
        var plateInfo = Find(archive, $"Metadata/plate_{plateNumber}.json");
        if (plateInfo is not null)
        {
            try
            {
                using var input = plateInfo.Open();
                using var json = JsonDocument.Parse(input);
                if (json.RootElement.TryGetProperty("filament_ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    usedIndices = ids.EnumerateArray().Where(e => e.TryGetInt32(out var id) && id >= 0).Select(e => e.GetInt32()).ToArray();
                if (json.RootElement.TryGetProperty("nozzle_diameter", out var element) && Number(element.ToString()) is > 0 and var parsed)
                {
                    var n = decimal.Round(parsed, 4);
                    if (nozzle is { } old && old != n) nozzleConflict = true;
                    else nozzle ??= n;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            { warnings.Add($"Plate {plateNumber}: plate metadata could not be read; using other embedded sources."); }
        }
        return new(nozzleConflict ? null : nozzle, minutes, weight, slots, materialsComplete, nozzleConflict, machine, usedIndices);
    }

    internal static string? NormalizeColor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var color = raw.TrimStart('#');
        return "#" + (color.Length >= 6 ? color[..6] : color).ToUpperInvariant();
    }

    internal static ZipArchiveEntry? Find(ZipArchive archive, string name) => archive.Entries.FirstOrDefault(e =>
        string.Equals(e.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));
    private static decimal? Number(string? raw) => decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
        out var n) && n >= 0 ? n : null;
}
