using OpenFarm.Application.Printers;

namespace OpenFarm.Application.Production;

public enum FeedPath
{
    Ams,
    External,
}

/// <summary>A spool's physical feed: an AMS unit and slot, or the external path (external port, or a reloader's feeding slot).</summary>
public readonly record struct FeedPlacement(FeedPath Path, int UnitIndex, int SlotPosition);

/// <summary>One job line: the file filaments (tools) it covers and where its spool is fed from.</summary>
public sealed record MappedLine(IReadOnlyList<int> Tools, FeedPlacement Placement);

/// <summary>
/// Builds the printer's filament mapping for a job. Tray ids follow Bambu Studio's encoding: a 4-slot AMS (or AMS Lite) is
/// <c>unit * 4 + slot</c>, a single-slot unit (AMS HT, ids from 128) is its unit id. The external spool is never a tray id:
/// the legacy array holds -1 and only <c>ams_mapping2</c> names it.
/// </summary>
public static class PrintFeedMappingBuilder
{
    private const int FirstSingleSlotUnit = 128;
    private const int Unmapped = 255;

    /// <summary>
    /// An AMS has to load its own filament and the external spool has to be hand-loaded into the extruder, so one job
    /// can't draw from both; the readiness check holds such a job.
    /// </summary>
    public static bool MixesAmsWithExternal(IEnumerable<FeedPlacement> placements)
    {
        var paths = placements.Select(p => p.Path).Distinct().Take(2).Count();
        return paths > 1;
    }

    /// <summary>
    /// Null when the file's filaments can't be matched to the lines (so the caller must not guess a mapping) or the lines mix
    /// AMS with external. A job with one line and no known tool is read as the file's first filament.
    /// </summary>
    public static FilamentFeedMapping? Build(IReadOnlyList<MappedLine> lines, int filamentCount)
    {
        if (lines.Count == 0 || MixesAmsWithExternal(lines.Select(l => l.Placement))) return null;
        IReadOnlyList<MappedLine> resolved = lines.Count == 1 && lines[0].Tools.Count == 0 ? [lines[0] with { Tools = [0] }] : lines;
        if (resolved.All(l => l.Tools.Count == 0)) return null;

        var size = Math.Max(filamentCount, resolved.SelectMany(l => l.Tools).Max() + 1);
        var trays = Enumerable.Repeat(-1, size).ToArray();
        var targets = Enumerable.Repeat(new FeedTarget(Unmapped, Unmapped), size).ToArray();
        foreach (var line in resolved)
            foreach (var tool in line.Tools)
            {
                var p = line.Placement;
                if (p.Path == FeedPath.External)
                {
                    targets[tool] = new FeedTarget(Unmapped, 0);
                    continue;
                }

                var single = p.UnitIndex >= FirstSingleSlotUnit;
                trays[tool] = single ? p.UnitIndex : p.UnitIndex * 4 + p.SlotPosition;
                targets[tool] = new FeedTarget(p.UnitIndex, single ? 0 : p.SlotPosition);
            }

        return new FilamentFeedMapping(resolved[0].Placement.Path == FeedPath.Ams, trays, targets);
    }
}
