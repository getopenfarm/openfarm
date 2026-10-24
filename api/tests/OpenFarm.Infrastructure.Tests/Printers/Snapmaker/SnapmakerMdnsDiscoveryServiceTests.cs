using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Tests.Printers.Snapmaker;

public class SnapmakerMdnsDiscoveryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly IPAddress Sender = IPAddress.Parse("10.0.0.9");

    // The keys u1-moonraker's zeroconf component publishes.
    private static readonly string[] U1Txt =
        ["sn=SNU1TEST0001", "machine_type=Snapmaker U1", "device_name=Bay 4", "ip=10.0.0.77", "version=1.3.0", "link_mode=lan", "userid=", "region=us"];

    [Fact]
    public void Parse_U1Reply_ReadsTheU1FromItsTxtAndTheSendersAddress()
    {
        var (printer, goodbye) = SnapmakerMdnsDiscoveryService.Parse(U1Reply(), Sender, Now).Should().ContainSingle().Subject;

        goodbye.Should().BeFalse();
        printer.Should().Be(new DiscoveredPrinter("SNU1TEST0001", "Bay 4", "Snapmaker", "U1", "Snapmaker U1", "10.0.0.9", Now));
    }

    [Fact]
    public void Parse_LegacyUnicastReply_SkipsTheEchoedQuestion() =>
        SnapmakerMdnsDiscoveryService.Parse(U1Reply(echoQuestion: true), Sender, Now).Should().ContainSingle();

    [Fact]
    public void Parse_TtlZero_IsAGoodbye() =>
        SnapmakerMdnsDiscoveryService.Parse(U1Reply(ttl: 0), Sender, Now).Should().ContainSingle().Which.Goodbye.Should().BeTrue();

    [Fact]
    public void Parse_NoDeviceName_NamesItBySerial() =>
        SnapmakerMdnsDiscoveryService.Parse(U1Reply(txt: ["SN=SNU1TEST0001", "machine_type=Snapmaker U1"]), Sender, Now)
            .Should().ContainSingle().Which.Printer.Name.Should().Be("SNU1TEST0001");

    [Theory]
    [InlineData("machine_type=Snapmaker J1")] // a Snapmaker OpenFarm can't drive
    [InlineData("machine_type=Bambu Lab P1S")] // catalogued, but not a Snapmaker
    [InlineData("device_name=Bay 4")] // no machine type at all
    public void Parse_NotAKnownSnapmakerModel_IsSkipped(string entry) =>
        SnapmakerMdnsDiscoveryService.Parse(U1Reply(txt: ["sn=SN1", entry]), Sender, Now).Should().BeEmpty();

    [Fact]
    public void Parse_QueryOrMalformedPacket_ReturnsEmpty()
    {
        byte[] response = [0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0];
        byte[][] packets =
        [
            SnapmakerMdnsDiscoveryService.Query, // a query, not a response
            U1Reply()[..^3], // truncated inside its last record
            U1Reply()[..11], // shorter than a header
            [.. response, 40, (byte)'a'], // a label running past the end
            [.. response, 0, 0, 16, 0, 1, 0, 0, 0, 120, 0, 3, 5, (byte)'a', (byte)'b'], // a TXT string overrunning its record
        ];

        packets.Should().AllSatisfy(packet => SnapmakerMdnsDiscoveryService.Parse(packet, Sender, Now).Should().BeEmpty());
    }

    [Fact]
    public async Task Announcement_ListsTheU1_AndItsGoodbyeRemovesIt()
    {
        using var responder = Bind(); // never answers: the announcement alone lists the U1
        var options = Loopback(responder);
        using var service = await StartAsync(options);
        using var printer = Bind();

        await EventuallyAsync(() => service.GetRecent().Count == 1, async () => await printer.SendAsync(U1Reply(), options.Listen));
        service.GetRecent().Single().IpAddress.Should().Be("127.0.0.1");

        await EventuallyAsync(() => service.GetRecent().Count == 0, async () => await printer.SendAsync(U1Reply(ttl: 0), options.Listen));
    }

    [Fact]
    public async Task Browse_QueriesOnlyOnceTheListIsRead_RequeriesAndStopsWhenNobodyReads()
    {
        using var responder = Bind();
        var options = Loopback(responder) with
        {
            FirstRequery = TimeSpan.FromMilliseconds(100),
            MaxRequery = TimeSpan.FromMilliseconds(200),
            BrowseIdle = TimeSpan.FromMilliseconds(600),
        };
        using var service = await StartAsync(options);

        await Task.Delay(500);
        responder.Available.Should().Be(0, "nobody has read the list");

        service.GetRecent();
        var query = await ReceiveAsync(responder);
        query.Buffer.Should().Equal(ExpectedQuery);
        await responder.SendAsync(U1Reply(), query.RemoteEndPoint);
        (await ReceiveAsync(responder)).Buffer.Should().Equal(ExpectedQuery, "a browse re-queries");

        await Task.Delay(2000);
        while (responder.Available > 0)
        {
            await responder.ReceiveAsync();
        }

        await Task.Delay(1000);
        responder.Available.Should().Be(0, "queries stop once nobody reads the list");
        service.GetRecent().Should().ContainSingle().Which.SerialNumber.Should().Be("SNU1TEST0001");
    }

    [Fact]
    public async Task SharedPortTaken_QueriesFromAnEphemeralPort_AndTheUnicastAnswerListsTheU1()
    {
        using var responder = Bind();
        using var squatter = Bind(); // bound without address reuse, so the port can't be shared
        var options = Loopback(responder) with { Listen = (IPEndPoint)squatter.Client.LocalEndPoint! };
        using var service = await StartAsync(options);

        service.GetRecent();
        var query = await ReceiveAsync(responder);
        query.RemoteEndPoint.Port.Should().NotBe(options.Listen.Port);
        await responder.SendAsync(U1Reply(), query.RemoteEndPoint);

        await EventuallyAsync(() => service.GetRecent().Count == 1);
    }

    private static byte[] ExpectedQuery =>
        [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, .. ServiceName, 0, 12, 0, 1];

    private static byte[] ServiceName => [.. Label("_snapmaker"), .. Label("_tcp"), .. Label("local"), 0];

    /// <summary>
    /// A U1's answer laid out the way python-zeroconf sends it: the PTR answer, then SRV, TXT and A as additionals, every
    /// name after the first compressed against it. A legacy unicast answer also echoes the question.
    /// </summary>
    private static byte[] U1Reply(uint ttl = 4500, bool echoQuestion = false, string[]? txt = null)
    {
        const int service = 12; // the first name, right after the header
        List<byte> message = [0, 0, 0x84, 0, 0, echoQuestion ? (byte)1 : (byte)0, 0, 1, 0, 0, 0, 3];
        byte[] ptrOwner = ServiceName;
        if (echoQuestion)
        {
            message.AddRange([.. ServiceName, 0, 12, 0, 1]);
            ptrOwner = Pointer(service);
        }

        var instance = Record(message, ptrOwner, 12, ttl, [.. Label("U1-TEST0001"), .. Pointer(service)]);
        // SRV: priority, weight, port 1884, then the host name, which ends in "local" (the service name's third label).
        var host = Record(message, Pointer(instance), 33, ttl, [0, 0, 0, 0, 0x07, 0x5C, .. Label("U1-TEST0001"), .. Pointer(service + 16)]) + 6;
        Record(message, Pointer(instance), 16, ttl, [.. (txt ?? U1Txt).SelectMany(Label)]);
        Record(message, Pointer(host), 1, ttl, [10, 0, 0, 77]);
        return [.. message];
    }

    /// <summary>Appends a resource record (class IN, cache-flush) and returns where its data starts.</summary>
    private static int Record(List<byte> message, byte[] owner, ushort type, uint ttl, byte[] data)
    {
        message.AddRange(owner);
        message.AddRange([(byte)(type >> 8), (byte)type, 0x80, 1, (byte)(ttl >> 24), (byte)(ttl >> 16), (byte)(ttl >> 8), (byte)ttl]);
        message.AddRange([(byte)(data.Length >> 8), (byte)data.Length]);
        var start = message.Count;
        message.AddRange(data);
        return start;
    }

    private static byte[] Label(string text) => [(byte)Encoding.UTF8.GetByteCount(text), .. Encoding.UTF8.GetBytes(text)];

    private static byte[] Pointer(int offset) => [(byte)(0xC0 | (offset >> 8)), (byte)offset];

    private static UdpClient Bind() => new(new IPEndPoint(IPAddress.Loopback, 0));

    private static SnapmakerMdnsOptions Loopback(UdpClient responder)
    {
        int port;
        using (var probe = Bind())
        {
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }

        return new SnapmakerMdnsOptions
        {
            Listen = new IPEndPoint(IPAddress.Loopback, port),
            Group = null,
            QueryTo = (IPEndPoint)responder.Client.LocalEndPoint!,
        };
    }

    private static async Task<SnapmakerMdnsDiscoveryService> StartAsync(SnapmakerMdnsOptions options)
    {
        var service = new SnapmakerMdnsDiscoveryService(NullLogger<SnapmakerMdnsDiscoveryService>.Instance, options);
        await service.StartAsync(CancellationToken.None);
        return service;
    }

    private static Task<UdpReceiveResult> ReceiveAsync(UdpClient socket) => socket.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

    // The service binds in the background, so an early announcement can be lost: send until it lands.
    private static async Task EventuallyAsync(Func<bool> condition, Func<Task>? attempt = null)
    {
        for (var tries = 0; tries < 100; tries++)
        {
            if (attempt is not null)
            {
                await attempt();
            }

            await Task.Delay(50);
            if (condition())
            {
                return;
            }
        }

        throw new TimeoutException("Not met within 5 s.");
    }
}
