using System.Net;
using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuSsdpDiscoveryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_BambuNotify_ReturnsMappedPrinter()
    {
        // Captured from a real P1S (firmware 01.10.00.00) on the 239.255.255.250:1990 multicast.
        const string payload =
            "NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nServer: UPnP/1.0\r\nLocation: 192.168.1.42\r\n" +
            "NT: urn:bambulab-com:device:3dprinter:1\r\nUSN: 01P00A123456789\r\nCache-Control: max-age=1800\r\n" +
            "DevModel.bambu.com: C12\r\nDevName.bambu.com: P1S-Bay3\r\nDevSignal.bambu.com: -50\r\n" +
            "DevConnect.bambu.com: lan\r\nDevBind.bambu.com: free\r\nDevseclink.bambu.com: secure\r\n" +
            "DevVersion.bambu.com: 01.10.00.00\r\nDevCap.bambu.com: 1\r\n\r\n";

        var printer = BambuSsdpDiscoveryService.Parse(payload, IPAddress.Parse("10.0.0.9"), Now);

        printer.Should().NotBeNull();
        printer!.SerialNumber.Should().Be("01P00A123456789");
        printer.Name.Should().Be("P1S-Bay3");
        printer.ModelCode.Should().Be("P1S");
        printer.RawModel.Should().Be("C12");
        printer.IpAddress.Should().Be("192.168.1.42");
        printer.LastSeenAt.Should().Be(Now);
    }

    [Fact]
    public void Parse_UnknownModelWithoutLocation_FallsBackToSenderAndNullModel()
    {
        var printer = BambuSsdpDiscoveryService.Parse(
            "NOTIFY * HTTP/1.1\r\nUSN: SER1\r\nDevModel.bambu.com: O1D\r\n\r\n", IPAddress.Parse("10.0.0.9"), Now);

        printer!.IpAddress.Should().Be("10.0.0.9");
        printer.ModelCode.Should().BeNull();
        printer.Name.Should().Be("SER1");
    }

    [Fact]
    public void Parse_NonBambuSsdp_ReturnsNull()
    {
        BambuSsdpDiscoveryService.Parse(
            "NOTIFY * HTTP/1.1\r\nLocation: http://10.0.0.5/desc.xml\r\nUSN: uuid:abc::upnp:rootdevice\r\n\r\n", IPAddress.Loopback, Now)
            .Should().BeNull();
    }
}
