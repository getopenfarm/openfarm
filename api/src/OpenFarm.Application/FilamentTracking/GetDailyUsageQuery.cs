using OpenFarm.Application.Common;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>One filament's grams drawn per colour per local day, over the last <see cref="Days"/> days in <paramref name="TimeZone"/>.</summary>
public sealed record GetDailyUsageQuery(Guid FilamentId, TimeZoneInfo TimeZone)
{
    public const int Days = 30;
}

/// <summary>A print or manual draw from one of the filament's spools; <see cref="Waste"/> when its print failed or was cancelled.</summary>
public sealed record UsageEntry(Guid FilamentVariantId, DateTimeOffset RecordedAtUtc, decimal Grams, bool Waste);

/// <summary>Grams a colour drew each day, oldest first, and the part of them failed and cancelled prints took.</summary>
public sealed record ColorDailyUsage(Guid FilamentVariantId, IReadOnlyList<decimal> DrawnGrams, IReadOnlyList<decimal> WasteGrams);

/// <summary>The local days, oldest first, and each colour that drew filament on any of them.</summary>
public sealed record DailyUsage(IReadOnlyList<DateOnly> Days, IReadOnlyList<ColorDailyUsage> Colors)
{
    /// <summary>
    /// Sums <paramref name="entries"/> into the <paramref name="days"/> local days ending <paramref name="today"/>. An entry's day is its
    /// instant converted to <paramref name="timeZone"/>; no local midnight is converted to UTC, since some do not exist.
    /// </summary>
    public static DailyUsage Bucket(IEnumerable<UsageEntry> entries, TimeZoneInfo timeZone, DateOnly today, int days = GetDailyUsageQuery.Days)
    {
        var first = today.AddDays(1 - days);
        var colors = new Dictionary<Guid, (decimal[] Drawn, decimal[] Waste)>();
        foreach (var entry in entries)
        {
            var index = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.RecordedAtUtc, timeZone).DateTime).DayNumber - first.DayNumber;
            if (index < 0 || index >= days) continue;
            if (!colors.TryGetValue(entry.FilamentVariantId, out var sums))
                colors[entry.FilamentVariantId] = sums = (new decimal[days], new decimal[days]);
            sums.Drawn[index] += entry.Grams;
            if (entry.Waste) sums.Waste[index] += entry.Grams;
        }

        return new(
            Enumerable.Range(0, days).Select(first.AddDays).ToList(),
            colors.Select(c => new ColorDailyUsage(c.Key, c.Value.Drawn, c.Value.Waste)).ToList());
    }
}

public sealed class GetDailyUsageQueryHandler(ISpoolReadModel spoolReadModel) : IQueryHandler<GetDailyUsageQuery, DailyUsage>
{
    public async Task<DailyUsage> HandleAsync(GetDailyUsageQuery query, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, query.TimeZone).DateTime);
        // One day more than the window covers its first local day in any zone.
        // ponytail: the entries are bucketed in memory; group in SQL if a filament draws tens of thousands of times a month.
        var entries = await spoolReadModel.ListUsageEntriesAsync(
            query.FilamentId, now.AddDays(-(GetDailyUsageQuery.Days + 1)), cancellationToken);
        return DailyUsage.Bucket(entries, query.TimeZone, today);
    }
}
