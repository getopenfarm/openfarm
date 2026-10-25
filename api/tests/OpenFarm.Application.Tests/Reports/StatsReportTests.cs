using FluentAssertions;
using OpenFarm.Application.Reports;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.Reports;

public class StatsReportTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateOnly Oct24 = new(2026, 10, 24);
    private static readonly DateOnly Oct25 = new(2026, 10, 25);

    // Oct 24 and Oct 25 in Berlin; the clocks go back on Oct 25, so it lasts 25 hours.
    private static readonly GetStatsQuery TwoDays = new(
        new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), Berlin);

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LocalDays_AreTheDatesTheRangeTouches_ADstDayIncluded()
    {
        StatsReport.LocalDays(TwoDays.From, TwoDays.To, Berlin).Should().Equal(Oct24, Oct25);
        StatsReport.LocalDays(TwoDays.From, TwoDays.From, Berlin).Should().BeEmpty();
    }

    [Fact]
    public void Days_AreOldestFirst_AndADayWithNothingIsZero()
    {
        var data = Data(printsByDay: [(Oct25, new StatsPrints(3, 1, 0, 300, 25, 6, 1.5))],
            activityByDay: [(Oct25, PrinterActivity.Printing, 10), (Oct25, PrinterActivity.Idle, 5), (Oct25, PrinterActivity.Unknown, 2)]);

        var report = StatsReport.Of(TwoDays, data, Data(), Now);

        report.Days.Select(d => d.Day).Should().Equal(Oct24, Oct25);
        report.Days[0].Should().Be(new StatsDay(Oct24, 0, 0, 0, 0, 0, 0, 0, 0));
        report.Days[1].Should().Be(new StatsDay(Oct25, 3, 1, 0, 300, 25, 6, 10, 15));
    }

    [Fact]
    public void TheSummary_CountsCancelledAgainstSuccess_AndWasteAgainstAllGrams()
    {
        var data = Data(printsByDay: [(Oct24, new StatsPrints(6, 1, 1, 750, 50, 12, 3)), (Oct25, new StatsPrints(2, 0, 0, 200, 0, 4, 0))]);

        var summary = StatsReport.Of(TwoDays, data, Data(), Now).Current;

        summary.Prints.Should().Be(10);
        summary.SuccessRate.Should().Be(0.8);
        summary.UsedGrams.Should().Be(950);
        summary.WasteShare.Should().Be(0.05);
        summary.Units.Should().Be(16);
        summary.WasteMachineHours.Should().Be(3);
    }

    [Fact]
    public void Utilization_IsPrintingOverObservedTime_AndUntrackedTimeIsNoData()
    {
        // Created at the start of Oct 25: 25 hours of the range existed; 20 were tracked, 2 of those while OpenFarm was down.
        var printer = new StatsPrinter(Guid.NewGuid(), "P01", new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero));
        var data = Data(printers: [printer], activityByPrinter: [
            (printer.Id, PrinterActivity.Printing, 12), (printer.Id, PrinterActivity.Paused, 1), (printer.Id, PrinterActivity.Offline, 3),
            (printer.Id, PrinterActivity.Unknown, 2), (printer.Id, PrinterActivity.Idle, 2)]);

        var report = StatsReport.Of(TwoDays, data, Data(printers: [printer]), Now);

        var activity = report.Printers.Single().Activity;
        activity.Should().Be(new ActivityHours(12, 1, 0, 2, 3, 2 + 5));
        activity.Utilization.Should().Be(12.0 / 18);
        report.Current.Activity.Should().Be(activity);
    }

    [Fact]
    public void ThePreviousSummary_ComesFromThePreviousRange()
    {
        var previous = Data(printsByDay: [(new DateOnly(2026, 10, 23), new StatsPrints(1, 1, 0, 10, 10, 1, 0.5))]);

        var report = StatsReport.Of(TwoDays, Data(), previous, Now);

        report.Current.Prints.Should().Be(0);
        report.Current.SuccessRate.Should().BeNull();
        report.Previous.Prints.Should().Be(2);
        report.Previous.SuccessRate.Should().Be(0.5);
    }

    [Fact]
    public async Task TheHandler_ReadsTheRange_AndTheSameSpanJustBeforeIt()
    {
        var readModel = new RecordingReadModel();

        await new GetStatsQueryHandler(readModel).HandleAsync(TwoDays, default);

        readModel.Ranges.Select(r => (r.From, r.To, r.TimeZoneId)).Should().Equal(
            (TwoDays.From, TwoDays.To, "Europe/Berlin"),
            (TwoDays.From - (TwoDays.To - TwoDays.From), TwoDays.From, "Europe/Berlin"));
    }

    private static StatsData Data(
        IReadOnlyList<(DateOnly, StatsPrints)>? printsByDay = null,
        IReadOnlyList<(DateOnly, PrinterActivity, double)>? activityByDay = null,
        IReadOnlyList<(Guid, PrinterActivity, double)>? activityByPrinter = null,
        IReadOnlyList<StatsPrinter>? printers = null) =>
        new(printsByDay ?? [], [], activityByDay ?? [], activityByPrinter ?? [], printers ?? [], new StatsMedians(null, null, null, null), []);

    private sealed class RecordingReadModel : IStatsReadModel
    {
        public List<StatsRange> Ranges { get; } = [];

        public Task<StatsData> ReadAsync(StatsRange range, CancellationToken cancellationToken)
        {
            Ranges.Add(range);
            return Task.FromResult(Data());
        }
    }
}
