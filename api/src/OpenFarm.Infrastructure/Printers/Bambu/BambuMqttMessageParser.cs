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
            if (!document.RootElement.TryGetProperty("print", out var print))
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
            return document.RootElement.TryGetProperty("info", out var info)
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
            return document.RootElement.TryGetProperty("print", out _);
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
            if (!document.RootElement.TryGetProperty("print", out var print))
            {
                return null;
            }

            var nozzleTempC = TryGetDecimal(print, "nozzle_temper");
            var bedTempC = TryGetDecimal(print, "bed_temper");
            var printProgressPercent = TryGetInt(print, "mc_percent");
            var currentStageLabel = TryGetInt(print, "stg_cur") is { } stage ? CurrentStageIds.Resolve(stage) : null;
            var amsUnits = ParseAmsUnits(print);

            return new PrinterTelemetrySnapshot(nozzleTempC, bedTempC, printProgressPercent, currentStageLabel, amsUnits);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<AmsUnit> ParseAmsUnits(JsonElement print)
    {
        if (!print.TryGetProperty("ams", out var amsRoot)
            || !amsRoot.TryGetProperty("ams", out var amsArray)
            || amsArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var units = new List<AmsUnit>();
        foreach (var unitElement in amsArray.EnumerateArray())
        {
            if (!unitElement.TryGetProperty("id", out var idProperty)
                || idProperty.ValueKind != JsonValueKind.String
                || !int.TryParse(idProperty.GetString(), out var unitIndex))
            {
                continue;
            }

            var trays = new List<AmsTray>();
            if (unitElement.TryGetProperty("tray", out var trayArray) && trayArray.ValueKind == JsonValueKind.Array)
            {
                trays.AddRange(trayArray.EnumerateArray().Select(ParseTray));
            }

            units.Add(new AmsUnit(unitIndex, trays));
        }

        return units;
    }

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

    private static decimal? TryGetDecimal(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value)
            ? value
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
}
