using FluentAssertions;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public sealed class PrinterNozzleTests
{
    [Fact]
    public void NozzleIsUnknownUntilReportedAndPreservedAcrossConnections()
    {
        var printer = Printer.Create(Guid.NewGuid(), "P1", "Bambu", "P1S", "127.0.0.1", "SN");
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
}
