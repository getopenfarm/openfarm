using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Production;

/// <summary>One job line: the file filaments (tools) it covers and the feed slot its spool is loaded from, as the printer sees it.</summary>
public sealed record MappedLine(IReadOnlyList<int> Tools, FeedSlotRef Slot);

/// <summary>Builds the printer's filament mapping for a job: which feed slot each of the file's filaments comes from.</summary>
public static class PrintFeedMappingBuilder
{
    /// <summary>
    /// An AMS has to load its own filament and the external spool has to be hand-loaded into the extruder, so one job
    /// can't draw from both; the readiness check holds such a job.
    /// </summary>
    // ponytail: printer-wide, as every catalogued model has one inlet; per extruder once a model has several
    public static bool MixesAmsWithExternal(IEnumerable<FeedSlotRef> slots) =>
        slots.Select(s => s.IsMultiMaterialUnit).Distinct().Take(2).Count() > 1;

    /// <summary>
    /// Null when the file's filaments can't be matched to the lines (so the caller must not guess a mapping) or the lines mix
    /// AMS with external. A job with one line and no known tool is read as the file's first filament.
    /// </summary>
    public static FilamentFeedMapping? Build(IReadOnlyList<MappedLine> lines, int filamentCount)
    {
        if (lines.Count == 0 || MixesAmsWithExternal(lines.Select(l => l.Slot))) return null;
        IReadOnlyList<MappedLine> resolved = lines.Count == 1 && lines[0].Tools.Count == 0 ? [lines[0] with { Tools = [0] }] : lines;
        if (resolved.All(l => l.Tools.Count == 0)) return null;

        var tools = new FeedSlotRef?[Math.Max(filamentCount, resolved.SelectMany(l => l.Tools).Max() + 1)];
        foreach (var line in resolved)
            foreach (var tool in line.Tools)
                tools[tool] = line.Slot;

        return new FilamentFeedMapping(tools);
    }
}
