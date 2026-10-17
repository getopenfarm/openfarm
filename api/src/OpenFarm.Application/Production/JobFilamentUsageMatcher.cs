namespace OpenFarm.Application.Production;

/// <summary>What the slicer says one filament (tool) of the dispatched file uses, in grams.</summary>
public sealed record FilamentUsage(string? MaterialType, string? ColorHex, decimal Grams, int ToolIndex = 0);

/// <summary>A job line's share of the dispatched file: its grams and the tools those grams were extruded by.</summary>
public sealed record LineUsage(decimal? Grams, IReadOnlyList<int> Tools);

/// <summary>Assigns the dispatched file's per-filament usage to a job's requirement lines.</summary>
public static class JobFilamentUsageMatcher
{
    /// <summary>
    /// One entry per line (null grams when the file says nothing about it). A usage goes to the first line with the same
    /// material and colour; a line whose colour was overridden away from the file's still gets the whole file's usage
    /// when it is the job's only line, since there is then no other line it could belong to.
    /// </summary>
    public static IReadOnlyList<LineUsage> Match(
        IReadOnlyList<(string? MaterialType, string? ColorHex)> lines, IReadOnlyList<FilamentUsage> usages)
    {
        var grams = new decimal?[lines.Count];
        var tools = lines.Select(_ => new List<int>()).ToArray();
        foreach (var usage in usages)
        {
            var index = FindIndex(lines, usage);
            if (index < 0) continue;
            grams[index] = (grams[index] ?? 0) + usage.Grams;
            tools[index].Add(usage.ToolIndex);
        }

        if (lines.Count == 1 && grams[0] is null && usages.Count > 0)
        {
            grams[0] = usages.Sum(u => u.Grams);
            tools[0].AddRange(usages.Select(u => u.ToolIndex));
        }

        return grams.Select((g, i) => new LineUsage(g, tools[i])).ToList();
    }

    private static int FindIndex(IReadOnlyList<(string? MaterialType, string? ColorHex)> lines, FilamentUsage usage)
    {
        for (var i = 0; i < lines.Count; i++)
            if (string.Equals(lines[i].MaterialType, usage.MaterialType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(lines[i].ColorHex, usage.ColorHex, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
