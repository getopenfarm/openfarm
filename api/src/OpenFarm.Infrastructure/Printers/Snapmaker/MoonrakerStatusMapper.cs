using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>
/// Pure translation of a Snapmaker U1's merged Moonraker status (the <c>printer.objects.subscribe</c> snapshot with
/// every <c>notify_status_update</c> delta folded in) into OpenFarm's printer vocabulary: Bambu-style
/// <c>gcode_state</c> words, the four toolheads as one four-slot unit whose slot n is the spool on head (extruder) n,
/// and exceptions as alerts. Object and field names follow Snapmaker's open-source Klipper/Moonraker forks
/// (<c>u1-klipper</c>, <c>u1-moonraker</c>).
/// </summary>
internal static class MoonrakerStatusMapper
{
    public const int HeadCount = 4;

    public const string AlertSource = "Exception";

    private static readonly string[] ExtruderFields = ["temperature", "nozzle_diameter", "state"];

    /// <summary>The objects and fields <c>printer.objects.subscribe</c> asks for.</summary>
    public static IReadOnlyDictionary<string, string[]> Subscription { get; } = new Dictionary<string, string[]>
    {
        ["exclude_object"] = ["excluded_objects", "objects", "current_object"],
        ["print_stats"] = ["state", "filename", "print_duration", "info"],
        ["virtual_sdcard"] = ["progress"],
        ["display_status"] = ["progress"],
        ["heater_bed"] = ["temperature"],
        ["extruder"] = ExtruderFields,
        ["extruder1"] = ExtruderFields,
        ["extruder2"] = ExtruderFields,
        ["extruder3"] = ExtruderFields,
        ["toolhead"] = ["extruder"],
        ["webhooks"] = ["state"],
        ["print_task_config"] = ["filament_type", "filament_sub_type", "filament_color_rgba", "filament_exist"],
    };

    /// <summary>OpenFarm's state word for <c>print_stats.state</c>; null for anything unrecognized.</summary>
    public static string? State(JsonObject status) => Text(status["print_stats"], "state") switch
    {
        "standby" => "IDLE",
        "printing" => "RUNNING",
        "paused" => "PAUSE",
        "complete" => "FINISH",
        "cancelled" or "error" => "FAILED",
        _ => null,
    };

    /// <summary>Klippy shut down or failed: any print in progress is lost.</summary>
    public static bool KlippyDown(JsonObject status) => Text(status["webhooks"], "state") is "shutdown" or "error";

    /// <summary>M73 progress when the file reports it, else the file position; 0–100.</summary>
    public static int? ProgressPercent(JsonObject status)
    {
        var progress = Number(status["display_status"], "progress") is > 0 and var m73 ? m73 : Number(status["virtual_sdcard"], "progress");
        return progress is { } p ? (int)Math.Round(Math.Clamp(p, 0m, 1m) * 100, MidpointRounding.AwayFromZero) : null;
    }

    /// <param name="state">The state word this snapshot reports, from <see cref="State"/> after the connector's transition rules.</param>
    public static PrinterTelemetrySnapshot Telemetry(JsonObject status, string? state)
    {
        var heads = Enumerable.Range(0, HeadCount).Select(i => status[ExtruderName(i)]).ToArray();
        var active = ActiveHead(status, heads);
        var temperatures = heads.Select(h => Number(h, "temperature")).ToArray();
        var reportedDiameters = heads.Select(h => Number(h, "nozzle_diameter")).ToArray();
        var diameters = reportedDiameters.Select(d => d is > 0 ? Math.Round(d.Value, 2, MidpointRounding.AwayFromZero) : (decimal?)null).ToList();
        var printing = state is "RUNNING" or "PAUSE";
        var progress = printing || state == "FINISH" ? ProgressPercent(status) : null;
        var file = Text(status["print_stats"], "filename") is { Length: > 0 } name ? name : null;
        var info = status["print_stats"]?["info"];

        return new PrinterTelemetrySnapshot(
            // With no head picked up the hottest one is what an operator needs to see while the heads cool down.
            WholeDegrees(active is { } a ? temperatures[a] : temperatures.Max()),
            WholeDegrees(Number(status["heater_bed"], "temperature")),
            progress,
            CurrentStageLabel: null,
            // Null would keep the last print's countdown forever; nothing remains once the printer isn't printing.
            printing ? RemainingMinutes(Number(status["print_stats"], "print_duration"), progress) : 0,
            file is null ? null : Path.GetFileNameWithoutExtension(file),
            Toolheads(status["print_task_config"], active),
            ExcludedObjectIds: status["exclude_object"]?["excluded_objects"] is JsonArray excluded ? excluded.Select(x => x!.ToString().ToUpperInvariant()).ToArray() : null,
            CurrentLayer: Integer(info, "current_layer"),
            TotalLayers: Integer(info, "total_layer"),
            // Only the picked-up head is feeding; a parked head's spool waits.
            ActiveSlots: Enumerable.Range(0, HeadCount).Select(head => head == active ? HeadSlot(head) : (FeedSlotRef?)null).ToList(),
            CurrentGcodeFile: file,
            // Per head: each head is confirmed at its own nozzle.
            NozzleDiametersMm: reportedDiameters.Any(d => d is not null) ? diameters : null,
            ExtruderTempsC: temperatures.Any(t => t is not null) ? temperatures.Select(WholeDegrees).ToList() : null);
    }

    /// <summary>The feed slot of a head: the toolheads are one unit whose slot n is the spool on head n.</summary>
    public static FeedSlotRef HeadSlot(int head) => new(FeedUnitKindCatalog.MultiMaterialUnit, 0, head);

    /// <summary>Alerts for the exception list Moonraker caches (<c>server.exception.query</c> / <c>notify_exception_status</c>).</summary>
    public static IReadOnlyList<PrinterAlert> Alerts(JsonNode? exceptions) =>
        exceptions is JsonArray list
            ? list.OfType<JsonObject>()
                // id:index:code — two heads raising the same code are two alerts.
                .Select(e => new PrinterAlert(AlertSource, $"{e["id"]}:{e["index"]}:{e["code"]}", Severity(e["level"]),
                    Text(e, "message") is { Length: > 0 } message ? message : "Printer exception."))
                .Distinct()
                .ToList()
            : [];

    private static string ExtruderName(int head) => head == 0 ? "extruder" : $"extruder{head}";

    private static int? HeadIndex(string extruderName) =>
        extruderName == "extruder" ? 0
        : extruderName.StartsWith("extruder", StringComparison.Ordinal)
          && int.TryParse(extruderName["extruder".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var head)
          && head is > 0 and < HeadCount ? head
        : null;

    private static int? ActiveHead(JsonObject status, JsonNode?[] heads)
    {
        for (var head = 0; head < heads.Length; head++)
            if (Text(heads[head], "state") == "ACTIVATE") return head;

        // ponytail: firmware without per-head state falls back to the selected extruder, which may be a parked head.
        return heads.All(h => h?["state"] is null) && Text(status["toolhead"], "extruder") is { } name ? HeadIndex(name) : null;
    }

    private static IReadOnlyList<FeedUnitReading> Toolheads(JsonNode? config, int? active)
    {
        // An empty list means "unchanged" to Printer.RecordTelemetry, which is right until print_task_config arrives.
        if (config is not JsonObject) return [];

        var slots = Enumerable.Range(0, HeadCount).Select(head =>
        {
            var empty = Element(config["filament_exist"], head)?.GetValueKind() != JsonValueKind.True;
            var type = empty ? null : Known(Element(config["filament_type"], head));
            var subType = Known(Element(config["filament_sub_type"], head));
            var color = Element(config["filament_color_rgba"], head)?.ToString();
            return new FeedSlotReading(head, empty, type,
                type is null || string.IsNullOrWhiteSpace(color) ? null : color.ToUpperInvariant(),
                // Bambu's "PLA Basic" shape, which RfidVariantMatch.SameLine compares against a spool's line.
                type is null || subType is null ? null : $"{type} {subType}",
                RemainPercent: null,
                Active: active == head,
                ExtruderIndex: head);
        }).ToList();
        return [new FeedUnitReading(FeedUnitKindCatalog.MultiMaterialUnit, 0, slots)];
    }

    // ponytail: the level scale is unpublished (raised exceptions default to 3); confirm against real captures.
    private static string Severity(JsonNode? level) =>
        level is JsonValue value && value.TryGetValue<int>(out var number)
            ? number switch { >= 3 => "Error", 2 => "Warning", 1 => "Information", _ => "Unknown" }
            : "Unknown";

    private static int? RemainingMinutes(decimal? printDurationSeconds, int? progressPercent) =>
        printDurationSeconds is > 0 and var elapsed && progressPercent is > 0 and var percent
            ? (int)Math.Ceiling(elapsed * (100 - percent) / percent / 60)
            : null;

    private static string? Known(JsonNode? value) =>
        value?.ToString() is { } text && !string.IsNullOrWhiteSpace(text) && !text.Equals("NONE", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;

    private static JsonNode? Element(JsonNode? array, int index) =>
        array is JsonArray items && index < items.Count ? items[index] : null;

    private static string? Text(JsonNode? node, string property) =>
        node is JsonObject obj && obj[property] is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    // Through the JSON text, so a number reads the same whether it was parsed or set in memory as an int or a double.
    private static decimal? Number(JsonNode? node, string property) =>
        node is JsonObject obj && obj[property] is JsonValue value && value.GetValueKind() == JsonValueKind.Number
        && decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static int? Integer(JsonNode? node, string property) =>
        Number(node, property) is { } number ? (int)number : null;

    private static decimal? WholeDegrees(decimal? value) =>
        value is { } v ? Math.Round(v, 0, MidpointRounding.AwayFromZero) : null;
}
