using FluentAssertions;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.Printers;

public class GetDiscoveredPrintersQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_MergesEverySource_AndMatchesRegisteredSerialsIgnoringCase()
    {
        var bambu = new DiscoveredPrinter("01P00A123456789", "P1S-Bay3", "Bambu", "P1S", "C12", "10.0.0.5", Now);
        var u1 = new DiscoveredPrinter("snu1test0001", "Bay 4", "Snapmaker", "U1", "Snapmaker U1", "10.0.0.9", Now);
        var registered = Printer.Create("Bay 4", "Snapmaker", "U1", "10.0.0.9", "SNU1TEST0001");
        var handler = new GetDiscoveredPrintersQueryHandler(
            [new FixedDiscovery(bambu), new FixedDiscovery(u1)], new FakePrinterRepository(registered));

        var results = await handler.HandleAsync(new GetDiscoveredPrintersQuery(), CancellationToken.None);

        results.Should().Equal(new DiscoveredPrinterResult(u1, AlreadyRegistered: true), new DiscoveredPrinterResult(bambu, AlreadyRegistered: false));
    }

    private sealed class FixedDiscovery(params DiscoveredPrinter[] printers) : IPrinterDiscovery
    {
        public IReadOnlyList<DiscoveredPrinter> GetRecent() => printers;
    }
}
