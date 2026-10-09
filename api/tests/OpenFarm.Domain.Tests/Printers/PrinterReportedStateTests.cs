using FluentAssertions;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public class PrinterReportedStateTests
{
    [Theory]
    [InlineData("IDLE", PrinterStatus.Idle)]
    [InlineData("INIT", PrinterStatus.Printing)]
    [InlineData("PREPARE", PrinterStatus.Printing)]
    [InlineData("SLICING", PrinterStatus.Printing)]
    [InlineData("RUNNING", PrinterStatus.Printing)]
    [InlineData("PAUSE", PrinterStatus.Printing)]
    [InlineData("FINISH", PrinterStatus.AwaitingClear)]
    [InlineData("FAILED", PrinterStatus.AwaitingClear)]
    public void ReportedState_IsSeparateFromReadiness(string state, PrinterStatus expected)
    {
        var printer = Create();
        var at = DateTimeOffset.UtcNow;
        printer.ObserveReportedState(state, at).Should().BeTrue();
        printer.ObserveStatus(state);
        printer.Status.Should().Be(expected);
        printer.ReportedState.Should().Be(state);
        printer.StateObservedAtUtc.Should().Be(at);
        printer.ObserveReportedState(state, at.AddSeconds(1)).Should().BeFalse();
        printer.StateObservedAtUtc.Should().Be(at.AddSeconds(1));
    }

    [Fact]
    public void UnknownAndIdleReports_DoNotBypassAwaitingClear()
    {
        var printer = Create();
        printer.ObserveStatus("FINISH");
        printer.ObserveReportedState("NEW_FIRMWARE_STATE", DateTimeOffset.UtcNow);
        printer.ObserveStatus("NEW_FIRMWARE_STATE").Should().BeFalse();
        printer.ReportedState.Should().Be("NEW_FIRMWARE_STATE");
        printer.ObserveReportedState("IDLE", DateTimeOffset.UtcNow);
        printer.ObserveStatus("IDLE").Should().BeFalse();
        printer.Status.Should().Be(PrinterStatus.AwaitingClear);
        printer.TransitionTo(PrinterStatus.Idle);
        printer.Status.Should().Be(PrinterStatus.Idle);
    }

    [Fact]
    public void Alerts_MergeSourcesIgnoreOrderAndRetainOffline()
    {
        var printer = Create();
        var warning = new PrinterAlert("Hms", "A", "Warning", "Warning");
        var info = new PrinterAlert("Hms", "B", "Information", "Information");
        var error = new PrinterAlert("PrintError", "C", "Error", "Error");
        printer.ObserveAlerts([warning, info], [error]).Should().BeTrue();
        printer.ObserveAlerts([info, warning, warning], null).Should().BeFalse();
        printer.ObserveAlerts(null, null).Should().BeFalse();
        printer.MarkUnreachable();
        printer.ActiveAlerts.Should().HaveCount(3);
        printer.ObserveAlerts([], null).Should().BeTrue();
        printer.ActiveAlerts.Should().Equal(error);
        printer.ObserveAlerts(null, []).Should().BeTrue();
        printer.ActiveAlerts.Should().BeEmpty();
    }

    private static Printer Create() => Printer.Create(Guid.NewGuid(), "Printer", "Bambu", "P1S", "10.0.0.1", "SN");
}
