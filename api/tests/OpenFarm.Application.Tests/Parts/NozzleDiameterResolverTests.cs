using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.Parts;

public sealed class NozzleDiameterResolverTests
{
    private static Printer Printer(decimal? nozzle)
    {
        var printer = OpenFarm.Domain.Printers.Printer.Create("P1", "Bambu", "P1S", "127.0.0.1", Guid.NewGuid().ToString());
        printer.ObserveNozzle(nozzle, DateTimeOffset.UtcNow);
        return printer;
    }
    [Fact]
    public void UsesTelemetryWithoutMachineProfileAndChoosesMode()
    {
        NozzleDiameterResolver.Resolve([Printer(0.6m), Printer(0.6m), Printer(0.4m)]).Should().Be(0.6m);
        NozzleDiameterResolver.Resolve([Printer(0.6m), Printer(0.4m)]).Should().Be(0.4m);
    }
    [Fact]
    public void UnknownOrUnconfirmedNozzlesAreExcluded()
    {
        var printer = Printer(0.6m); printer.BeginConnection();
        NozzleDiameterResolver.Resolve([printer, Printer(null)]).Should().BeNull();
    }
}
