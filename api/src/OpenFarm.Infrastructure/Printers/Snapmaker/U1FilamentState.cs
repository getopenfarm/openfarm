using System.Globalization;
using System.Text.Json.Nodes;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>U1 sensor/RFID state. Missing deltas retain observations; an explicit unload clears held descriptions.</summary>
internal sealed class U1FilamentState
{
    private static readonly HashSet<string> Materials = ["PLA", "PETG", "PETG-CF", "ABS", "ASA", "TPU", "PVA", "PLA-CF", "PA", "PA-CF", "PA6-CF", "PA6-GF", "PC"];
    private readonly JsonObject[] _info = [new(), new(), new(), new()];
    private readonly bool?[] _loaded = new bool?[4];
    private readonly FeedSlotReading?[] _held = new FeedSlotReading?[4];
    private bool _observed;
    internal JsonNode? Overrides { get; set; }

    internal void Merge(JsonNode? delta)
    {
        if (delta?["filament_detect"]?["info"] is JsonArray info)
        {
            _observed = true;
            for (var i = 0; i < Math.Min(4, info.Count); i++)
                if (info[i] is JsonObject fields)
                    foreach (var (key, value) in fields) _info[i][key] = value?.DeepClone();
        }
        for (var i = 0; i < 4; i++)
            if (delta?[i < 2 ? "filament_feed left" : "filament_feed right"]?[$"extruder{i}"]?["filament_detected"] is JsonValue v && v.TryGetValue<bool>(out var loaded))
                _loaded[i] = loaded;
    }

    internal IReadOnlyList<FeedUnitReading> Read(int? active)
    {
        if (!_observed) return [];
        var slots = new List<FeedSlotReading>();
        for (var i = 0; i < 4; i++)
        {
            var rfid = _info[i];
            var main = rfid["MAIN_TYPE"]?.ToString();
            var official = !string.IsNullOrWhiteSpace(main) && main != "NONE";
            var type = official ? main : At("filament_type", i);
            type = type?.Trim().ToUpperInvariant() switch { "NONE" => null, "PETG-HF" => "PETG", "PC-ABS" => "PC", "PA-GF" => "PA6-GF", var other => other };
            if (type is not null && !Materials.Contains(type)) type = null;
            var sub = official ? rfid["SUB_TYPE"]?.ToString() : At("filament_sub_type", i);
            if (sub == "NONE") sub = null;
            var color = official ? Color(rfid) : At("filament_color_rgba", i)?.ToUpperInvariant();
            var loaded = _loaded[i] ?? official;
            var reading = new FeedSlotReading(i, !loaded, type, color, string.IsNullOrWhiteSpace(sub) ? null : $"{type} {sub}", null, i == active, RfidId: official ? Uid(rfid["CARD_UID"]) : null, ExtruderIndex: i);
            if (string.IsNullOrWhiteSpace(type) && loaded && _held[i] is { } held)
                reading = held with { Empty = false, Active = i == active };
            else _held[i] = loaded && !string.IsNullOrWhiteSpace(type) ? reading : null;
            slots.Add(reading);
        }
        return [new FeedUnitReading(FeedUnitKindCatalog.MultiMaterialUnit, 0, slots)];
    }

    private string? At(string field, int index) => Overrides?[field] is JsonArray a && index < a.Count ? a[index]?.ToString() : null;
    private static long? Number(JsonNode? n) => long.TryParse(n?.ToString(), CultureInfo.InvariantCulture, out var value) ? value : null;
    private static string? Color(JsonObject info)
    {
        if (Number(info["RGB_1"]) is >= 0 and var rgb) return $"{rgb & 0xFFFFFF:X6}{Math.Clamp(Number(info["ALPHA"]) ?? 255, 0, 255):X2}";
        if (Number(info["ARGB_COLOR"]) is >= 0 and var argb) return $"{argb & 0xFFFFFF:X6}{(argb >> 24) & 255:X2}";
        return null;
    }
    private static string? Uid(JsonNode? value)
    {
        var text = value is JsonArray a ? string.Concat(a.Select(n => $"{(Number(n) ?? 0) & 255:X2}"))
            : Number(value) is { } n ? $"{n & 0xFFFFFFFF:X8}" : null;
        return text is null || text.All(c => c == '0') ? null : text;
    }
}
