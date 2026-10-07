using System.Globalization;
using System.Text;

namespace OpenFarm.Infrastructure.Production;

internal static class PrintFileName
{
    private static readonly object TimestampLock = new();
    private static long _lastTimestamp;

    internal static string Describe(string partName, IEnumerable<string?> materials, int? minutes)
    {
        var components = new List<string> { WithoutExtension(partName) };
        components.AddRange(materials.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).Select(m => m!));
        if (minutes is > 0)
        {
            components.Add(minutes >= 60
                ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}h{minutes % 60}m")
                : string.Create(CultureInfo.InvariantCulture, $"{minutes}m"));
        }
        return string.Join('_', components);
    }

    internal static string ForBambu(string jobName)
    {
        // Millisecond timestamps stay distinct even for concurrent uploads or a clock adjustment.
        long timestamp;
        lock (TimestampLock)
        {
            timestamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _lastTimestamp + 1);
            _lastTimestamp = timestamp;
        }
        var stem = new StringBuilder();
        foreach (var c in WithoutExtension(jobName))
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '-') stem.Append(c);
            else if (stem.Length > 0 && stem[^1] != '_') stem.Append('_');
            if (stem.Length >= 100) break;
        }
        var safeName = stem.ToString().Trim('_', '-');
        if (safeName.Length == 0) safeName = "Print";
        var suffix = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        return $"{safeName}_{suffix}.gcode.3mf";
    }

    private static string WithoutExtension(string name)
    {
        foreach (var extension in new[] { ".gcode.3mf", ".gcode", ".3mf", ".stl" })
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name[..^extension.Length];
        }
        return name;
    }
}
