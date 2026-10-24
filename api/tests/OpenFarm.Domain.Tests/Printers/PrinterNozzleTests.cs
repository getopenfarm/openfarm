using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public sealed class PrinterNozzleTests
{
    [Fact]
    public void NozzleIsUnknownUntilReportedAndPreservedAcrossConnections()
    {
        var printer = Printer.Create("P1", "Bambu", "P1S", "127.0.0.1", "SN");
        printer.ReportedNozzleDiameterMm.Should().BeNull();
        printer.NozzleConfirmed.Should().BeFalse();
        var now = DateTimeOffset.UtcNow;
        printer.ObserveNozzle(0.6m, now);
        printer.ReportedNozzleDiameterMm.Should().Be(0.6m);
        printer.NozzleObservedAtUtc.Should().Be(now);
        printer.NozzleConfirmed.Should().BeTrue();
        printer.BeginConnection();
        printer.ReportedNozzleDiameterMm.Should().Be(0.6m);
        printer.NozzleConfirmed.Should().BeFalse();
        printer.ObserveNozzle(null, now);
        printer.ObserveNozzle(0, now);
        printer.NozzleConfirmed.Should().BeFalse();
        printer.ObserveNozzle(0.4m, now.AddSeconds(1));
        printer.ReportedNozzleDiameterMm.Should().Be(0.4m);
        printer.NozzleConfirmed.Should().BeTrue();
        printer.MarkUnreachable();
        printer.NozzleConfirmed.Should().BeFalse();
    }

    [Fact]
    public void ObserveNozzles_AllExtrudersAlike_ConfirmsTheirNozzle()
    {
        var printer = Printer.Create("U1", "Snapmaker", "U1", "127.0.0.1", "SN");
        var now = DateTimeOffset.UtcNow;

        printer.ObserveNozzles([0.4m, 0.4m, 0.4m, 0.4m], now);

        printer.ReportedNozzleDiameterMm.Should().Be(0.4m);
        printer.NozzleObservedAtUtc.Should().Be(now);
        printer.NozzleConfirmed.Should().BeTrue();
        printer.Extruders.Select(e => (e.Index, e.NozzleDiameterMm)).Should().Equal((0, 0.4m), (1, 0.4m), (2, 0.4m), (3, 0.4m));
    }

    [Fact]
    public void MixedReport_ConfirmsEachExtruderWithItsOwnNozzle()
    {
        var printer = Printer.Create("U1", "Snapmaker", "U1", "127.0.0.1", "SN");

        printer.ObserveNozzles([0.4m, 0.4m, 0.6m, 0.4m], DateTimeOffset.UtcNow);

        printer.ConfirmedNozzleMm(0).Should().Be(0.4m);
        printer.ConfirmedNozzleMm(2).Should().Be(0.6m);
        printer.ConfirmedNozzlesMm().Should().Equal(0.4m, 0.6m);
        printer.NozzleConfirmed.Should().BeTrue("some extruder is confirmed");
        printer.ReportedNozzleDiameterMm.Should().BeNull("the extruders share no one nozzle");
    }

    [Fact]
    public void UnknownDiameter_UnconfirmsOnlyThatExtruder()
    {
        var printer = Printer.Create("U1", "Snapmaker", "U1", "127.0.0.1", "SN");
        printer.ObserveNozzles([0.4m, 0.4m, 0.4m, 0.4m], DateTimeOffset.UtcNow);

        printer.ObserveNozzles([0.4m, 0.4m, null, 0.4m], DateTimeOffset.UtcNow);

        printer.ConfirmedNozzleMm(2).Should().BeNull();
        printer.Extruders[2].NozzleDiameterMm.Should().Be(0.4m, "the last known diameter is kept for display");
        printer.ConfirmedNozzleMm(3).Should().Be(0.4m);
        printer.ConfirmedNozzlesMm().Should().Equal(0.4m);
    }

    [Fact]
    public void NewConnection_UnconfirmsEveryExtruder_KeepingLastDiameters()
    {
        var printer = Printer.Create("U1", "Snapmaker", "U1", "127.0.0.1", "SN");
        printer.ObserveNozzles([0.4m, 0.6m], DateTimeOffset.UtcNow);

        printer.BeginConnection();

        printer.ConfirmedNozzlesMm().Should().BeEmpty();
        printer.NozzleConfirmed.Should().BeFalse();
        printer.Extruders.Select(e => e.NozzleDiameterMm).Should().Equal(0.4m, 0.6m);
        printer.ReportedNozzlesMm().Should().Equal(0.4m, 0.6m);
    }

    [Fact]
    public void ObserveNozzles_KeepsTheSlotEachExtruderIsFeeding()
    {
        var printer = Printer.Create("P1", "Bambu", "P1S", "127.0.0.1", "SN");
        var feeding = new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 2);
        printer.RecordTelemetry(null, null, null, null, null, null, [], activeSlots: [feeding]);

        printer.ObserveNozzles([0.6m], DateTimeOffset.UtcNow);

        printer.Extruders.Should().Equal(new ExtruderReading(0, 0.6m, feeding, NozzleConfirmed: true));
    }
}
