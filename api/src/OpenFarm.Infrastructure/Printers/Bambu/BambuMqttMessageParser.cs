using System.Text.Json;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Parses raw Bambu LAN MQTT JSON payloads (<c>device/{serial}/report</c>) into primitive facts —
/// never returns Domain types directly (that mapping belongs to <see cref="OpenFarm.Domain.Printers.PrinterStatusTransitions"/>).
/// This phase only extracts <c>gcode_state</c> (research.md §2); telemetry/AMS parsing is added in
/// Phase 4 (US2).
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
}
