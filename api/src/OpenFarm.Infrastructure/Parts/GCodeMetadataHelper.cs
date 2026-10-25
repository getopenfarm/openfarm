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

        // OrcaSlicer's HEADER_BLOCK reports these two on one colon-delimited summary line —
        // "; model printing time: 5m 56s; total estimated time: 12m 25s" — not the "; key = value"
        // convention every other comment uses, so CommentLineRegex above never matches it (confirmed
        // against the real bundled 2.3.1 binary's output; TryAdd below still prefers an "=" match if
        // one is ever present, e.g. in older fixtures).
        var modelPrintingTimeMatch = ModelPrintingTimeRegex().Match(content);
        if (modelPrintingTimeMatch.Success)
        {
            values.TryAdd("model printing time", modelPrintingTimeMatch.Groups[1].Value.Trim());
        }

        var totalEstimatedTimeMatch = TotalEstimatedTimeRegex().Match(content);
        if (totalEstimatedTimeMatch.Success)
        {
            values.TryAdd("total estimated time", totalEstimatedTimeMatch.Groups[1].Value.Trim());
        }

        var weight = WeightSummaryRegex().Match(content);
        if (weight.Success) values.TryAdd("total filament weight [g]", weight.Groups[1].Value.Trim());
        return values;
    }

    public static decimal? ParseNozzleDiameter(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("nozzle_diameter", out var raw)) return null;
        var nozzles = SplitList(raw).Select(v => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0 ? (decimal?)n : null).ToList();
        return nozzles.Count > 0 && nozzles.All(n => n is not null) && nozzles.Distinct().Count() == 1 ? nozzles[0] : null;
    }

    /// <summary>
    /// One entry per comma-separated slot in <c>filament_type</c>/<c>filament_colour</c> — parallel arrays, same convention this codebase's <c>BambuMqttMessageParser</c> AMS reports already use.
    /// With <paramref name="onlyUsed"/>, only the slots the print draws from — no longer parallel to the file's arrays, so not for callers that index by tool.
    /// </summary>
    public static IReadOnlyList<Application.Parts.DetectedFilamentSlot> ParseFilamentSlots(Dictionary<string, string> values, bool onlyUsed = false)
    {
        if (!values.TryGetValue("filament_type", out var typesRaw))
        {
            return [];
        }

        var types = SplitList(typesRaw);
        var colors = values.TryGetValue("filament_colour", out var coloursRaw) ? SplitList(coloursRaw)
            : values.TryGetValue("filament_color", out var colorsRaw) ? SplitList(colorsRaw)
            : [];

        var vendors = values.TryGetValue("filament_vendor", out var vendorsRaw) ? SplitList(vendorsRaw) : [];
        var used = onlyUsed ? UsedSlots(values, types.Count) : null;

        var slots = new List<Application.Parts.DetectedFilamentSlot>();
        for (var i = 0; i < types.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(types[i]) || used is not null && !used[i])
            {
                continue;
            }

            var color = i < colors.Count && !string.IsNullOrWhiteSpace(colors[i]) ? NormalizeColorHex(colors[i]) : null;
            var vendor = i < vendors.Count && !string.IsNullOrWhiteSpace(vendors[i]) ? vendors[i] : null;
            slots.Add(new Application.Parts.DetectedFilamentSlot(types[i], color, vendor));
        }

        return slots;
    }

    /// <summary>
    /// Which slots the print draws from, by the per-slot usage line (<c>filament used [g] = 1.19, 0.00, …</c>); null when the file
    /// has none, it doesn't line up with the slots, or nothing is used — then every slot counts.
    /// </summary>
    private static bool[]? UsedSlots(Dictionary<string, string> values, int slotCount)
    {
        var raw = values.GetValueOrDefault("filament used [g]") ?? values.GetValueOrDefault("filament used [mm]");
        if (raw is null) return null;
        var amounts = SplitList(raw);
        if (amounts.Count != slotCount) return null;
        var used = new bool[slotCount];
        for (var i = 0; i < slotCount; i++)
        {
            if (!decimal.TryParse(amounts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return null;
            used[i] = amount > 0;
        }

        return used.Any(u => u) ? used : null;
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
        var secondsMatch = DurationSecondsRegex().Match(raw);
        if (!hoursMatch.Success && !minutesMatch.Success && !secondsMatch.Success) return null;
        if (!TryDurationComponent(hoursMatch, out var hours) || !TryDurationComponent(minutesMatch, out var minutes)
            || !TryDurationComponent(secondsMatch, out var seconds)) return null;
        var totalMinutes = hours * 60L + minutes + seconds / 60L;
        if (totalMinutes == 0 && seconds > 0) totalMinutes = 1;
        return totalMinutes <= int.MaxValue ? (int)totalMinutes : null;
    }

    public static decimal? ParseFilamentWeightGrams(Dictionary<string, string> values)
    {
        var raw = values.GetValueOrDefault("total filament weight [g]") ?? values.GetValueOrDefault("filament used [g]");
        if (raw is null) return null;
        decimal total = 0;
        foreach (var value in SplitList(raw))
        {
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) || weight < 0) return null;
            try { total = checked(total + weight); }
            catch (OverflowException) { return null; }
        }
        return total;
    }

    private static bool TryDurationComponent(Match match, out int value)
    {
        value = 0;
        return !match.Success || int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out value);
    }

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
        raw.Split([',', ';'], StringSplitOptions.TrimEntries).Select(v => v.Trim('"')).ToList();

    private static string NormalizeColorHex(string raw)
    {
        var trimmed = raw.TrimStart('#');
        // Bambu sometimes reports 8-digit RGBA — keep just RGB for display purposes.
        return "#" + (trimmed.Length >= 6 ? trimmed[..6] : trimmed).ToUpperInvariant();
    }

    // Same-line whitespace only: OrcaSlicer writes "; notes = " (empty) right before "; nozzle_diameter", and a \s* after "="
    // would take that next line as the value, losing the key.
    [GeneratedRegex(@"^[ \t]*;[ \t]*([A-Za-z0-9_\[\]\(\) ]+?)[ \t]*=[ \t]*(.+?)[ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex CommentLineRegex();

    [GeneratedRegex(@";\s*total filament weight \[g\]\s*:\s*([^;\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WeightSummaryRegex();

    [GeneratedRegex(@"model printing time:\s*([^;\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModelPrintingTimeRegex();

    [GeneratedRegex(@"total estimated time:\s*([^;\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalEstimatedTimeRegex();

    [GeneratedRegex(@"(\d+)h")]
    private static partial Regex DurationHoursRegex();

    [GeneratedRegex(@"(\d+)m")]
    private static partial Regex DurationMinutesRegex();

    [GeneratedRegex(@"(\d+)s")]
    private static partial Regex DurationSecondsRegex();

    [GeneratedRegex(@";\s*thumbnail begin[^\n]*\n(.*?);\s*thumbnail end", RegexOptions.Singleline)]
    private static partial Regex ThumbnailBlockRegex();
}
