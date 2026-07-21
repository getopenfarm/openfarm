using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Shared byte-level parsing of OrcaSlicer/Bambu Studio G-code header/footer comments
/// (<c>; key = value</c> convention) and the embedded base64 thumbnail block — used by
/// <see cref="AlreadySlicedFileMetadataParser"/> for both a standalone <c>.gcode</c> file and each
/// plate's own embedded G-code inside a <c>.gcode.3mf</c> (research.md §1).
/// </summary>
public static partial class GCodeMetadataHelper
{
    public static Dictionary<string, string> ParseComments(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in CommentLineRegex().Matches(content))
        {
            var key = match.Groups[1].Value.Trim();
            var value = match.Groups[2].Value.Trim();
            values.TryAdd(key, value);
        }

        return values;
    }

    public static decimal? ParseNozzleDiameter(Dictionary<string, string> values) =>
        TryParseFirstDecimal(values, "nozzle_diameter");

    /// <summary>One entry per comma-separated slot in <c>filament_type</c>/<c>filament_colour</c> — parallel arrays, same convention this codebase's <c>BambuMqttMessageParser</c> AMS reports already use.</summary>
    public static IReadOnlyList<Application.Parts.DetectedFilamentSlot> ParseFilamentSlots(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("filament_type", out var typesRaw))
        {
            return [];
        }

        var types = SplitList(typesRaw);
        var colors = values.TryGetValue("filament_colour", out var coloursRaw) ? SplitList(coloursRaw)
            : values.TryGetValue("filament_color", out var colorsRaw) ? SplitList(colorsRaw)
            : [];

        var slots = new List<Application.Parts.DetectedFilamentSlot>();
        for (var i = 0; i < types.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(types[i]))
            {
                continue;
            }

            var color = i < colors.Count && !string.IsNullOrWhiteSpace(colors[i]) ? NormalizeColorHex(colors[i]) : null;
            slots.Add(new Application.Parts.DetectedFilamentSlot(types[i], color, null));
        }

        return slots;
    }

    /// <summary>"model printing time: 1h 23m; total estimated time: 1h 30m" — prefers the total.</summary>
    public static int? ParseEstimatedPrintTimeMinutes(Dictionary<string, string> values)
    {
        var raw = values.TryGetValue("total estimated time", out var total) ? total
            : values.TryGetValue("estimated printing time (normal mode)", out var normal) ? normal
            : values.TryGetValue("model printing time", out var model) ? model
            : null;

        if (raw is null)
        {
            return null;
        }

        var hoursMatch = DurationHoursRegex().Match(raw);
        var minutesMatch = DurationMinutesRegex().Match(raw);
        var hours = hoursMatch.Success ? int.Parse(hoursMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = minutesMatch.Success ? int.Parse(minutesMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return hours == 0 && minutes == 0 ? null : (hours * 60) + minutes;
    }

    public static decimal? ParseFilamentWeightGrams(Dictionary<string, string> values) =>
        TryParseFirstDecimal(values, "total filament weight [g]") ?? TryParseFirstDecimal(values, "filament used [g]");

    /// <summary>The <c>; thumbnail begin WxH SIZE</c> / base64 lines / <c>; thumbnail end</c> block real slicer G-code embeds — re-encoding isn't needed if the source is already PNG (research.md §8's "normalized at ingestion" is satisfied by the slicer always exporting PNG here).</summary>
    public static byte[]? ExtractEmbeddedThumbnailPng(string content)
    {
        var match = ThumbnailBlockRegex().Match(content);
        if (!match.Success)
        {
            return null;
        }

        var base64 = string.Concat(match.Groups[1].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart(';').Trim()));

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static List<string> SplitList(string raw) =>
        raw.Split(',', StringSplitOptions.TrimEntries).ToList();

    private static string NormalizeColorHex(string raw)
    {
        var trimmed = raw.TrimStart('#');
        // Bambu sometimes reports 8-digit RGBA — keep just RGB for display purposes.
        return "#" + (trimmed.Length >= 6 ? trimmed[..6] : trimmed).ToUpperInvariant();
    }

    private static decimal? TryParseFirstDecimal(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return null;
        }

        var first = SplitList(raw)[0];
        var numeric = NumericPrefixRegex().Match(first);
        return numeric.Success && decimal.TryParse(numeric.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^\s*;\s*([A-Za-z0-9_\[\]\(\) ]+?)\s*=\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex CommentLineRegex();

    [GeneratedRegex(@"(\d+)h")]
    private static partial Regex DurationHoursRegex();

    [GeneratedRegex(@"(\d+)m")]
    private static partial Regex DurationMinutesRegex();

    [GeneratedRegex(@"-?\d+(\.\d+)?")]
    private static partial Regex NumericPrefixRegex();

    [GeneratedRegex(@";\s*thumbnail begin[^\n]*\n(.*?);\s*thumbnail end", RegexOptions.Singleline)]
    private static partial Regex ThumbnailBlockRegex();
}
