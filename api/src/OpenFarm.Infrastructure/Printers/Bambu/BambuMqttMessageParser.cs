using System.Text.Json;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Parses raw Bambu LAN MQTT JSON payloads (<c>device/{serial}/report</c>) into primitive facts —
/// never returns Domain types directly for status (that mapping belongs to
/// <see cref="OpenFarm.Domain.Printers.PrinterStatusTransitions"/>). Telemetry/AMS parsing (this
/// phase, US2) returns the Application-shaped <see cref="PrinterTelemetrySnapshot"/> DTO directly,
/// since there's no further Domain-level interpretation of those fields beyond storing them.
/// </summary>
public static class BambuMqttMessageParser
{
    /// <summary>
    /// Extracts the raw <c>gcode_state</c> string from a <c>{"print": {...}}</c> report payload.
    /// Returns <see langword="false"/> for any other payload shape (e.g. a <c>get_version</c>
    /// response) or malformed JSON — never throws.
    /// </summary>
    public static bool TryParseGcodeState(string json, out string? gcodeState)
    {
        gcodeState = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("print", out var print)
                || print.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (print.TryGetProperty("gcode_state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String)
            {
                gcodeState = stateElement.GetString();
                return !string.IsNullOrEmpty(gcodeState);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Whether this payload is a <c>get_version</c> response (used by the verify handshake).</summary>
    public static bool IsGetVersionResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("info", out var info)
                && info.ValueKind == JsonValueKind.Object
                && info.TryGetProperty("command", out var command)
                && command.ValueKind == JsonValueKind.String
                && command.GetString() == "get_version";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Whether this payload is a full <c>print</c> status dump (present on every report message).</summary>
    public static bool IsPrintReport(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("print", out var print)
                && print.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts temperature/progress/stage/AMS telemetry from a <c>{"print": {...}}</c> report
    /// payload (research.md §3). Returns <see langword="null"/> for any other payload shape or
    /// malformed JSON — never throws. Per-field values are individually nullable since Bambu's
    /// telemetry pushes are deltas (research.md §1) — a field's absence here means "unchanged", not
    /// "cleared"; the caller (<c>PrinterConnectionBackgroundService</c>) is responsible for merging
    /// rather than overwriting blindly where that distinction matters.
    /// </summary>
    public static PrinterTelemetrySnapshot? TryParseTelemetry(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("print", out var print)
                || print.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var nozzleTempC = RoundToWholeDegree(TryGetDecimal(print, "nozzle_temper"));
            var bedTempC = RoundToWholeDegree(TryGetDecimal(print, "bed_temper"));
            var printProgressPercent = TryGetInt(print, "mc_percent");
            var currentStageLabel = ResolveCurrentStageLabel(print);
            var remainingTimeMinutes = TryGetInt(print, "mc_remaining_time");
            var currentJobName = ResolveCurrentJobName(print);
            var amsUnits = ParseAmsUnits(print);
            var externalSpoolTray = ParseExternalSpool(print);

            return new PrinterTelemetrySnapshot(nozzleTempC, bedTempC, printProgressPercent, currentStageLabel, remainingTimeMinutes, currentJobName, amsUnits, externalSpoolTray);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves <c>stg_cur</c> to a display label, correcting for a firmware boot quirk: an idle
    /// A1/P1 printer reports <c>stg_cur == 0</c> instead of the real idle sentinel <c>255</c> right
    /// after boot (ported from pybambu's <c>StageAction.print_update</c>, cross-checked against
    /// third_party/printers/bambulab/docs/unified/local-mqtt/state-schema/print-job.md).
    /// </summary>
    private static string? ResolveCurrentStageLabel(JsonElement print)
    {
        var stage = TryGetInt(print, "stg_cur");
        if (stage is not { } stageValue)
        {
            return null;
        }

        var printType = GetString(print, "print_type");
        if (stageValue == 0 && string.Equals(printType, "idle", StringComparison.OrdinalIgnoreCase))
        {
            stageValue = 255;
        }

        return CurrentStageIds.Resolve(stageValue);
    }

    /// <summary>
    /// Resolves the human-readable name of whatever the printer is currently printing — reported by
    /// the printer itself (<c>subtask_name</c>), so it reflects any print regardless of origin
    /// (Bambu Studio, the Handy app, an SD card, or a future OpenFarm dispatch), not a stored job
    /// record. Falls back to the <c>gcode_file</c> basename (sans directory/extension) when the
    /// printer sends only that, and treats an empty value as "no job" (null), preserving the delta
    /// semantics — a genuinely absent key still returns null and the caller keeps the last value.
    /// </summary>
    private static string? ResolveCurrentJobName(JsonElement print)
    {
        var subtaskName = GetString(print, "subtask_name");
        if (!string.IsNullOrWhiteSpace(subtaskName))
        {
            return subtaskName;
        }

        var gcodeFile = GetString(print, "gcode_file");
        if (string.IsNullOrWhiteSpace(gcodeFile))
        {
            return null;
        }

        var fileName = Path.GetFileNameWithoutExtension(gcodeFile);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static List<AmsUnit> ParseAmsUnits(JsonElement print)
    {
        if (!print.TryGetProperty("ams", out var amsRoot)
            || amsRoot.ValueKind != JsonValueKind.Object
            || !amsRoot.TryGetProperty("ams", out var amsArray)
            || amsArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var units = new List<AmsUnit>();
        foreach (var unitElement in amsArray.EnumerateArray())
        {
            if (unitElement.ValueKind != JsonValueKind.Object
                || !unitElement.TryGetProperty("id", out var idProperty)
                || idProperty.ValueKind != JsonValueKind.String
                || !int.TryParse(idProperty.GetString(), out var unitIndex))
            {
                continue;
            }

            var trays = new List<AmsTray>();
            if (unitElement.TryGetProperty("tray", out var trayArray) && trayArray.ValueKind == JsonValueKind.Array)
            {
                trays.AddRange(trayArray.EnumerateArray()
                    .Where(tray => tray.ValueKind == JsonValueKind.Object)
                    .Select(ParseTray));
            }

            units.Add(new AmsUnit(unitIndex, trays));
        }

        return units;
    }

    /// <summary>Reads <c>print.vt_tray</c> — the external/single spool holder, reported the same way regardless of an AMS being attached (FR-020, research.md §6). Mirrors <see cref="ParseTray"/>.</summary>
    private static AmsTray? ParseExternalSpool(JsonElement print) =>
        print.TryGetProperty("vt_tray", out var vtTray) && vtTray.ValueKind == JsonValueKind.Object
            ? ParseTray(vtTray)
            : null;

    private static AmsTray ParseTray(JsonElement trayElement)
    {
        var slotIndex = trayElement.TryGetProperty("id", out var idProperty)
            && idProperty.ValueKind == JsonValueKind.String
            && int.TryParse(idProperty.GetString(), out var id)
                ? id
                : 0;

        var materialType = GetString(trayElement, "tray_type");
        var colorHex = GetString(trayElement, "tray_color");
        var subBrand = GetString(trayElement, "tray_sub_brands");
        var remainPercent = TryGetInt(trayElement, "remain");

        // pybambu's AMSTray defaults color to "00000000" and type to empty when no spool is
        // loaded (research.md §3) — there's no explicit "empty" flag on the wire.
        var empty = string.IsNullOrEmpty(materialType) || colorHex == "00000000";

        return new AmsTray(
            slotIndex,
            empty,
            empty ? null : materialType,
            empty ? null : colorHex,
            empty ? null : subBrand,
            remainPercent is null or < 0 ? null : remainPercent,
            Active: false); // Active-tray detection deferred — not required by any FR this phase.
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// Bambu reports raw ADC sensor readings (e.g. <c>27.8125</c>) that jitter by a fraction of a
    /// degree on nearly every message — whole-degree precision is all a print-farm operator cares
    /// about, and rounding here collapses that sensor noise so downstream persistence/broadcast/UI
    /// don't churn on changes nobody can act on.
    /// </summary>
    private static decimal? RoundToWholeDegree(decimal? value) =>
        value is { } v ? Math.Round(v, 0, MidpointRounding.AwayFromZero) : null;

    private static decimal? TryGetDecimal(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value)
            ? value
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
}
