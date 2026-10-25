using FluentAssertions;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public class PrinterActivityTests
{
    // After every printer the run creates (Create stamps the real clock), so no report lands before a printer existed.
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddDays(1);

    [Fact]
    public void ANewPrinter_IsOfflineUntilItsFirstMessage()
    {
        var printer = Create();

        printer.Activity.Should().Be(PrinterActivity.Offline);
        printer.ActivitySinceUtc.Should().Be(printer.CreatedAtUtc);
        printer.ClosedActivitySpans.Should().BeEmpty();

        var heard = printer.CreatedAtUtc.AddMinutes(1);
        printer.MarkReachable(heard);

        printer.Activity.Should().Be(PrinterActivity.Idle);
        printer.ActivitySinceUtc.Should().Be(heard);
        printer.ClosedActivitySpans.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Activity = PrinterActivity.Offline, StartedAtUtc = printer.CreatedAtUtc, EndedAtUtc = heard });
    }

    [Fact]
    public void Running_PausedAndResumed_AreSeparateStretches()
    {
        var printer = Reachable();

        Report(printer, "RUNNING", T0.AddMinutes(1));
        Report(printer, "PAUSE", T0.AddMinutes(10));
        Report(printer, "RUNNING", T0.AddMinutes(12));

        printer.Activity.Should().Be(PrinterActivity.Printing);
        Spans(printer).Should().Equal(
            (PrinterActivity.Idle, T0, T0.AddMinutes(1)),
            (PrinterActivity.Printing, T0.AddMinutes(1), T0.AddMinutes(10)),
            (PrinterActivity.Paused, T0.AddMinutes(10), T0.AddMinutes(12)));
    }

    [Fact]
    public void FinishThenMarkReady_LeavesOneAwaitingClearStretch()
    {
        var printer = Reachable();
        Report(printer, "RUNNING", T0.AddMinutes(1));
        Report(printer, "FINISH", T0.AddMinutes(60));

        printer.Activity.Should().Be(PrinterActivity.AwaitingClear);
        printer.TransitionTo(PrinterStatus.Idle, T0.AddMinutes(75));

        printer.Activity.Should().Be(PrinterActivity.Idle);
        Spans(printer).Should().ContainSingle(s => s.Item1 == PrinterActivity.AwaitingClear)
            .Which.Should().Be((PrinterActivity.AwaitingClear, T0.AddMinutes(60), T0.AddMinutes(75)));
    }

    [Fact]
    public void AwaitingClear_HoldsWhileThePrinterIsUnreachable()
    {
        // OpenFarm's own state: only Mark ready or a new print ends it, so a dropped connection doesn't cut the wait short.
        var printer = Reachable();
        Report(printer, "FINISH", T0.AddMinutes(1));

        printer.MarkUnreachable();
        printer.MarkReachable(T0.AddHours(2));

        printer.Activity.Should().Be(PrinterActivity.AwaitingClear);
        printer.ActivitySinceUtc.Should().Be(T0.AddMinutes(1));
        Spans(printer).Should().NotContain(s => s.Item1 == PrinterActivity.Offline);
    }

    [Fact]
    public void MarkUnreachable_StartsOfflineWhenThePrinterWasLastHeard()
    {
        var printer = Reachable();
        Report(printer, "RUNNING", T0.AddMinutes(1));
        printer.MarkReachable(T0.AddMinutes(30));

        printer.MarkUnreachable();

        printer.Activity.Should().Be(PrinterActivity.Offline);
        printer.ActivitySinceUtc.Should().Be(T0.AddMinutes(30));
        Spans(printer).Last().Should().Be((PrinterActivity.Printing, T0.AddMinutes(1), T0.AddMinutes(30)));
    }

    [Fact]
    public void MarkUnwatched_RecordsUnknownFromLastHeardToStartup()
    {
        var printer = Reachable();
        Report(printer, "RUNNING", T0.AddMinutes(1));
        printer.MarkReachable(T0.AddMinutes(30));
        var startup = T0.AddHours(5);

        printer.MarkUnwatched(startup);

        printer.IsReachable.Should().BeFalse();
        printer.Activity.Should().Be(PrinterActivity.Offline);
        printer.ActivitySinceUtc.Should().Be(startup);
        Spans(printer).TakeLast(2).Should().Equal(
            (PrinterActivity.Printing, T0.AddMinutes(1), T0.AddMinutes(30)),
            (PrinterActivity.Unknown, T0.AddMinutes(30), startup));
    }

    [Fact]
    public void MarkUnwatched_KeepsAwaitingClearAndAnOfflineStretch()
    {
        var awaiting = Reachable();
        Report(awaiting, "FINISH", T0.AddMinutes(1));
        var offline = Reachable();
        offline.MarkUnreachable();
        var awaitingSpans = awaiting.ClosedActivitySpans.Count;
        var offlineSpans = offline.ClosedActivitySpans.Count;

        awaiting.MarkUnwatched(T0.AddHours(5));
        offline.MarkUnwatched(T0.AddHours(5));

        awaiting.Activity.Should().Be(PrinterActivity.AwaitingClear);
        awaiting.ClosedActivitySpans.Should().HaveCount(awaitingSpans);
        offline.Activity.Should().Be(PrinterActivity.Offline);
        offline.ClosedActivitySpans.Should().HaveCount(offlineSpans);
    }

    [Fact]
    public void AnEarlierTime_IsClampedAndAnEmptyStretchDropped()
    {
        var printer = Reachable();
        Report(printer, "RUNNING", T0.AddMinutes(10));

        // A report timed before the open stretch began can't make a negative stretch; the same instant makes none.
        Report(printer, "PAUSE", T0.AddMinutes(5));

        printer.Activity.Should().Be(PrinterActivity.Paused);
        printer.ActivitySinceUtc.Should().Be(T0.AddMinutes(10));
        printer.ClosedActivitySpans.Should().NotContain(s => s.EndedAtUtc <= s.StartedAtUtc);
        printer.ClosedActivitySpans.Should().NotContain(s => s.Activity == PrinterActivity.Printing);
    }

    [Fact]
    public void RepeatedReports_RecordNothing()
    {
        var printer = Reachable();
        Report(printer, "RUNNING", T0.AddMinutes(1));
        var spans = printer.ClosedActivitySpans.Count;

        Report(printer, "RUNNING", T0.AddMinutes(2));
        printer.MarkReachable(T0.AddMinutes(3));
        Report(printer, "IDLE", T0.AddMinutes(4));
        Report(printer, "FINISH", T0.AddMinutes(5));
        var awaiting = printer.ClosedActivitySpans.Count;
        Report(printer, "IDLE", T0.AddMinutes(6));

        printer.ClosedActivitySpans.Should().HaveCount(awaiting);
        awaiting.Should().Be(spans + 2);
        printer.Activity.Should().Be(PrinterActivity.AwaitingClear);
    }

    private static Printer Create() => Printer.Create("Printer", "Bambu", "P1S", "10.0.0.1", "SN");

    /// <summary>A printer heard at <see cref="T0"/>, idle since then.</summary>
    private static Printer Reachable()
    {
        var printer = Create();
        printer.MarkReachable(T0);
        return printer;
    }

    private static void Report(Printer printer, string state, DateTimeOffset atUtc)
    {
        printer.MarkReachable(atUtc);
        printer.ObserveReportedState(state, atUtc);
        printer.ObserveStatus(state, atUtc);
    }

    /// <summary>The closed stretches after the first, Offline from creation until <see cref="Reachable"/> heard the printer.</summary>
    private static List<(PrinterActivity, DateTimeOffset, DateTimeOffset)> Spans(Printer printer) =>
        printer.ClosedActivitySpans.Skip(1).Select(s => (s.Activity, s.StartedAtUtc, s.EndedAtUtc)).ToList();
}
