using OpenFarm.Application.Common;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Reports;

/// <summary>
/// The farm's stats over [<see cref="From"/>, <see cref="To"/>), by local day in <see cref="TimeZone"/>, with the same span
/// just before <see cref="From"/> for comparison. Output comes from prints, counted on the day they ended; time comes from
/// printer activity, so a print still open adds no hours.
/// </summary>
public sealed record GetStatsQuery(DateTimeOffset From, DateTimeOffset To, TimeZoneInfo TimeZone);

/// <summary>Hours a printer's activity took; <see cref="NoData"/> is time nothing was observed (before tracking, or OpenFarm down).</summary>
public sealed record ActivityHours(double Printing, double Paused, double AwaitingClear, double Idle, double Offline, double NoData)
{
    public static readonly ActivityHours None = new(0, 0, 0, 0, 0, 0);

    /// <summary>Time something was known about the printer, offline included.</summary>
    public double Observed => Printing + Paused + AwaitingClear + Idle + Offline;

    /// <summary>The share of observed time spent printing, 0 to 1; null with nothing observed.</summary>
    public double? Utilization => Observed > 0 ? Printing / Observed : null;

    /// <summary>
    /// Sums the hours of each activity. Time in <paramref name="availableHours"/> no stretch covers, and
    /// <see cref="PrinterActivity.Unknown"/> time, is no data.
    /// </summary>
    public static ActivityHours Of(IEnumerable<(PrinterActivity Activity, double Hours)> stretches, double availableHours)
    {
        var hours = stretches.GroupBy(s => s.Activity).ToDictionary(g => g.Key, g => g.Sum(s => s.Hours));
        double In(PrinterActivity activity) => hours.GetValueOrDefault(activity);
        var tracked = hours.Values.Sum();
        return new(In(PrinterActivity.Printing), In(PrinterActivity.Paused), In(PrinterActivity.AwaitingClear), In(PrinterActivity.Idle),
            In(PrinterActivity.Offline), In(PrinterActivity.Unknown) + Math.Max(0, availableHours - tracked));
    }
}

/// <summary>Prints that started and ended in a range: the outcomes, the filament they took, and the plates' units.</summary>
public sealed record StatsPrints(int Completed, int Failed, int Cancelled, decimal UsedGrams, decimal WasteGrams, int Units, double WasteHours)
{
    public static readonly StatsPrints None = new(0, 0, 0, 0, 0, 0, 0);

    public int Prints => Completed + Failed + Cancelled;

    public StatsPrints Plus(StatsPrints other) => new(Completed + other.Completed, Failed + other.Failed, Cancelled + other.Cancelled,
        UsedGrams + other.UsedGrams, WasteGrams + other.WasteGrams, Units + other.Units, WasteHours + other.WasteHours);
}

/// <summary>
/// A range's headline figures. Used grams are completed prints', waste is failed and cancelled ones' (as in the materials
/// report); <see cref="WasteMachineHours"/> is how long those ran. The medians are null with nothing to measure.
/// </summary>
public sealed record StatsSummary(
    int Prints,
    int Completed,
    int Failed,
    int Cancelled,
    decimal UsedGrams,
    decimal WasteGrams,
    double WasteMachineHours,
    int Units,
    ActivityHours Activity,
    double? QueueWaitP50Minutes,
    double? QueueWaitP95Minutes,
    double? EstimateRatio,
    double? PlateClearMinutes)
{
    /// <summary>Completed over all prints, cancelled ones counting against, 0 to 1; null with no prints.</summary>
    public double? SuccessRate => Prints > 0 ? (double)Completed / Prints : null;

    /// <summary>Waste over all the grams prints took, 0 to 1; null when they took none.</summary>
    public double? WasteShare => UsedGrams + WasteGrams is > 0 and var printed ? (double)(WasteGrams / printed) : null;
}

/// <summary>One local day: prints that ended on it, and printer hours that fell in it.</summary>
public sealed record StatsDay(
    DateOnly Day, int Completed, int Failed, int Cancelled, decimal UsedGrams, decimal WasteGrams, int Units, double PrintingHours, double ObservedHours);

public sealed record PrinterStats(Guid PrinterId, string Name, int Prints, int Completed, int Failed, int Cancelled, decimal UsedGrams, ActivityHours Activity);

/// <summary>A code the printer reported when prints failed, and how many failed prints carried it.</summary>
public sealed record FailureCode(string Code, string? Message, int Prints);

/// <summary>The range against the one before it, its local days oldest first, each current printer, and the top failure codes.</summary>
public sealed record StatsReport(
    StatsSummary Current,
    StatsSummary Previous,
    IReadOnlyList<StatsDay> Days,
    IReadOnlyList<PrinterStats> Printers,
    IReadOnlyList<FailureCode> FailureCodes)
{
    public static StatsReport Of(GetStatsQuery query, StatsData current, StatsData previous, DateTimeOffset nowUtc)
    {
        var printsByDay = current.PrintsByDay.ToDictionary(d => d.Day, d => d.Prints);
        var hoursByDay = current.ActivityByDay.ToLookup(a => a.Day);
        var days = LocalDays(query.From, query.To, query.TimeZone).Select(day =>
        {
            var prints = printsByDay.GetValueOrDefault(day) ?? StatsPrints.None;
            var hours = hoursByDay[day].ToList();
            return new StatsDay(day, prints.Completed, prints.Failed, prints.Cancelled, prints.UsedGrams, prints.WasteGrams, prints.Units,
                hours.Where(h => h.Activity == PrinterActivity.Printing).Sum(h => h.Hours),
                hours.Where(h => h.Activity != PrinterActivity.Unknown).Sum(h => h.Hours));
        }).ToList();

        var printsByPrinter = current.PrintsByPrinter.Where(p => p.PrinterId is not null).ToDictionary(p => p.PrinterId!.Value, p => p.Prints);
        var hoursByPrinter = current.ActivityByPrinter.ToLookup(a => a.PrinterId);
        var printers = current.Printers.Select(printer =>
        {
            var prints = printsByPrinter.GetValueOrDefault(printer.Id) ?? StatsPrints.None;
            var activity = ActivityHours.Of(hoursByPrinter[printer.Id].Select(a => (a.Activity, a.Hours)),
                AvailableHours(printer.CreatedAtUtc, query.From, query.To, nowUtc));
            return new PrinterStats(printer.Id, printer.Name, prints.Prints, prints.Completed, prints.Failed, prints.Cancelled, prints.UsedGrams, activity);
        }).ToList();

        var span = query.To - query.From;
        return new(
            Summary(current, query.From, query.To, nowUtc),
            Summary(previous, query.From - span, query.From, nowUtc),
            days,
            printers,
            current.FailureCodes);
    }

    /// <summary>The local dates the range touches, oldest first, as the database buckets them.</summary>
    public static IReadOnlyList<DateOnly> LocalDays(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo timeZone)
    {
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, timeZone).DateTime);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to.AddTicks(-1), timeZone).DateTime);
        return Enumerable.Range(0, Math.Max(0, last.DayNumber - first.DayNumber + 1)).Select(first.AddDays).ToList();
    }

    /// <summary>Hours of [<paramref name="from"/>, <paramref name="to"/>) the printer existed, up to now.</summary>
    private static double AvailableHours(DateTimeOffset createdAtUtc, DateTimeOffset from, DateTimeOffset to, DateTimeOffset nowUtc)
    {
        var start = createdAtUtc > from ? createdAtUtc : from;
        var end = nowUtc < to ? nowUtc : to;
        return end > start ? (end - start).TotalHours : 0;
    }

    private static StatsSummary Summary(StatsData data, DateTimeOffset from, DateTimeOffset to, DateTimeOffset nowUtc)
    {
        var prints = data.PrintsByDay.Aggregate(StatsPrints.None, (sum, day) => sum.Plus(day.Prints));
        var activity = ActivityHours.Of(data.ActivityByPrinter.Select(a => (a.Activity, a.Hours)),
            data.Printers.Sum(p => AvailableHours(p.CreatedAtUtc, from, to, nowUtc)));
        return new(prints.Prints, prints.Completed, prints.Failed, prints.Cancelled, prints.UsedGrams, prints.WasteGrams, prints.WasteHours,
            prints.Units, activity, data.Medians.QueueWaitP50Minutes, data.Medians.QueueWaitP95Minutes, data.Medians.EstimateRatio,
            data.Medians.PlateClearMinutes);
    }
}

/// <summary>A range read at <see cref="NowUtc"/>, when each printer's open stretch ends; days are local to <see cref="TimeZoneId"/> (IANA).</summary>
public sealed record StatsRange(DateTimeOffset From, DateTimeOffset To, string TimeZoneId, DateTimeOffset NowUtc);

public sealed record StatsMedians(double? QueueWaitP50Minutes, double? QueueWaitP95Minutes, double? EstimateRatio, double? PlateClearMinutes);

public sealed record StatsPrinter(Guid Id, string Name, DateTimeOffset CreatedAtUtc);

/// <summary>
/// One range, summed in the database: prints by local end day and by printer (null: printers since removed), activity hours
/// by local day and by printer, each current printer, the medians and the top failure codes.
/// </summary>
public sealed record StatsData(
    IReadOnlyList<(DateOnly Day, StatsPrints Prints)> PrintsByDay,
    IReadOnlyList<(Guid? PrinterId, StatsPrints Prints)> PrintsByPrinter,
    IReadOnlyList<(DateOnly Day, PrinterActivity Activity, double Hours)> ActivityByDay,
    IReadOnlyList<(Guid PrinterId, PrinterActivity Activity, double Hours)> ActivityByPrinter,
    IReadOnlyList<StatsPrinter> Printers,
    StatsMedians Medians,
    IReadOnlyList<FailureCode> FailureCodes);

public interface IStatsReadModel
{
    Task<StatsData> ReadAsync(StatsRange range, CancellationToken cancellationToken);
}

public sealed class GetStatsQueryHandler(IStatsReadModel readModel) : IQueryHandler<GetStatsQuery, StatsReport>
{
    public async Task<StatsReport> HandleAsync(GetStatsQuery query, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var span = query.To - query.From;
        var current = await readModel.ReadAsync(new StatsRange(query.From, query.To, query.TimeZone.Id, now), cancellationToken);
        var previous = await readModel.ReadAsync(new StatsRange(query.From - span, query.From, query.TimeZone.Id, now), cancellationToken);
        return StatsReport.Of(query, current, previous, now);
    }
}
