using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>Where the mDNS socket binds and queries, and the browse timings; tests point them at loopback.</summary>
internal sealed record SnapmakerMdnsOptions
{
    public IPEndPoint Listen { get; init; } = new(IPAddress.Any, 5353);

    /// <summary>The multicast group joined on <see cref="Listen"/>; null joins none.</summary>
    public IPAddress? Group { get; init; } = IPAddress.Parse("224.0.0.251");

    public IPEndPoint QueryTo { get; init; } = new(IPAddress.Parse("224.0.0.251"), 5353);

    // RFC 6762 §5.2 backoff, capped under StaleAfter so a live U1 that misses one query (Wi-Fi drops multicast) stays listed.
    public TimeSpan FirstRequery { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRequery { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Queries stop once nobody has read the list for this long.</summary>
    public TimeSpan BrowseIdle { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Finds Snapmaker U1s by mDNS: their Moonraker publishes <c>_snapmaker._tcp.local</c> with the serial, machine type and
/// device name in TXT. It listens on the shared mDNS port, so a U1's boot announcement and shutdown goodbye show at once,
/// and it queries only while someone reads the list (the Add printer page): at once, then backing off, then not at all
/// once nobody reads. A U1 announces only on boot or change, so a U1 that was already on is found by those queries.
/// When the mDNS port can't be shared it queries from an ephemeral port instead; the U1's responder (python-zeroconf)
/// answers those by unicast (RFC 6762 §6.7), so browsing still works, minus announcements. Like Bambu SSDP, it needs the
/// host on the printers' LAN — a bridged Docker container hears nothing.
/// </summary>
public sealed partial class SnapmakerMdnsDiscoveryService : BackgroundService, IPrinterDiscovery
{
    private const ushort TxtType = 16;

    // One question: _snapmaker._tcp.local, PTR, class IN with the unicast-response bit clear, so answers come multicast.
    internal static readonly byte[] Query =
    [
        0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, // id 0 (RFC 6762 §18.1), flags 0, one question
        10, .. "_snapmaker"u8, 4, .. "_tcp"u8, 5, .. "local"u8, 0,
        0, 12, 0, 1,
    ];

    private readonly ConcurrentDictionary<string, DiscoveredPrinter> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<bool> _reads = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    private readonly ILogger<SnapmakerMdnsDiscoveryService> _logger;
    private readonly SnapmakerMdnsOptions _options;
    private long _lastReadTicks;
    private long _lastQueryTicks;

    public SnapmakerMdnsDiscoveryService(ILogger<SnapmakerMdnsDiscoveryService> logger)
        : this(logger, new SnapmakerMdnsOptions())
    {
    }

    internal SnapmakerMdnsDiscoveryService(ILogger<SnapmakerMdnsDiscoveryService> logger, SnapmakerMdnsOptions options)
    {
        _logger = logger;
        _options = options;
    }

    /// <summary>Reading the list is what starts (or extends) a browse.</summary>
    public IReadOnlyList<DiscoveredPrinter> GetRecent()
    {
        Volatile.Write(ref _lastReadTicks, DateTimeOffset.UtcNow.UtcTicks);
        _reads.Writer.TryWrite(true);

        // Listed until it stays silent through a whole stale window of our queries. Between browses nothing is queried,
        // so a new browse lists what was heard before at once, and the first query sorts out who is still there.
        var cutoff = Volatile.Read(ref _lastQueryTicks) - _options.StaleAfter.Ticks;
        return _seen.Values.Where(p => p.LastSeenAt.UtcTicks >= cutoff).ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = Open();
        if (udp is not null)
        {
            await Task.WhenAll(ReceiveAsync(udp, stoppingToken), QueryAsync(udp, stoppingToken));
        }
    }

    // ponytail: the group join and the queries use the default interface only; add a socket per NIC if a multi-homed
    // host misses printers.
    private UdpClient? Open()
    {
        // Shares the port with the OS responder (mDNSResponder, avahi), as the Bambu listener shares 1990 with Bambu Studio.
        var shared = new UdpClient { ExclusiveAddressUse = false };
        try
        {
            shared.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            shared.Client.Bind(_options.Listen);
            if (_options.Group is not null)
            {
                shared.JoinMulticastGroup(_options.Group);
            }

            return shared;
        }
        catch (SocketException ex)
        {
            shared.Dispose();
            LogSharedPortUnavailable(ex, _options.Listen.Port);
        }

        try
        {
            return new UdpClient(new IPEndPoint(_options.Listen.Address, 0));
        }
        catch (SocketException ex)
        {
            LogSocketFailed(ex);
            return null;
        }
    }

    private async Task ReceiveAsync(UdpClient udp, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try
            {
                packet = await udp.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException ex)
            {
                LogReceiveFailed(ex);
                continue;
            }

            foreach (var (printer, goodbye) in Parse(packet.Buffer, packet.RemoteEndPoint.Address, DateTimeOffset.UtcNow))
            {
                if (goodbye)
                {
                    _seen.TryRemove(printer.SerialNumber, out _);
                }
                else
                {
                    _seen[printer.SerialNumber] = printer;
                }
            }
        }
    }

    private async Task QueryAsync(UdpClient udp, CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await _reads.Reader.ReadAsync(stoppingToken);
                for (var wait = _options.FirstRequery; Browsing(); wait = wait * 2 < _options.MaxRequery ? wait * 2 : _options.MaxRequery)
                {
                    try
                    {
                        await udp.SendAsync(Query, _options.QueryTo, stoppingToken);
                    }
                    catch (SocketException ex)
                    {
                        LogQueryFailed(ex);
                    }

                    Volatile.Write(ref _lastQueryTicks, DateTimeOffset.UtcNow.UtcTicks);
                    await Task.Delay(wait, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool Browsing() => DateTimeOffset.UtcNow.UtcTicks - Volatile.Read(ref _lastReadTicks) < _options.BrowseIdle.Ticks;

    /// <summary>
    /// The Snapmaker printers in one mDNS response, each flagged when it's a goodbye (TTL 0). A printer is a TXT record with
    /// an <c>sn</c> and a <c>machine_type</c> the catalog knows as a Snapmaker model, whatever its owner name: names are
    /// skipped, never followed, so a crafted packet can't loop. Empty for a query or a malformed packet.
    /// </summary>
    public static List<(DiscoveredPrinter Printer, bool Goodbye)> Parse(ReadOnlySpan<byte> message, IPAddress sender, DateTimeOffset now)
    {
        // Header: id, flags (top bit set in a response), then the question, answer, authority and additional counts.
        if (message.Length < 12 || (message[2] & 0x80) == 0)
        {
            return [];
        }

        var at = 12;
        for (var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]); questions > 0; questions--)
        {
            if (!SkipName(message, ref at))
            {
                return [];
            }

            at += 4; // type, class
        }

        List<(DiscoveredPrinter Printer, bool Goodbye)> found = [];
        var records = BinaryPrimitives.ReadUInt16BigEndian(message[6..]) + BinaryPrimitives.ReadUInt16BigEndian(message[8..])
            + BinaryPrimitives.ReadUInt16BigEndian(message[10..]);
        for (var i = 0; i < records; i++)
        {
            // Name, type, class, TTL, data length, data.
            if (!SkipName(message, ref at) || at + 10 > message.Length)
            {
                return [];
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(message[at..]);
            var ttl = BinaryPrimitives.ReadUInt32BigEndian(message[(at + 4)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(message[(at + 8)..]);
            at += 10;
            if (at + length > message.Length)
            {
                return [];
            }

            // ponytail: a reply without its TXT record is skipped; probe /machine/system_info if real U1s answer that way.
            if (type == TxtType)
            {
                if (ReadTxt(message.Slice(at, length)) is not { } txt)
                {
                    return [];
                }

                if (ToPrinter(txt, sender, now) is { } printer)
                {
                    found.Add((printer, ttl == 0));
                }
            }

            at += length;
        }

        return found;
    }

    // A name is labels ending in an empty one or in a 2-byte pointer to an earlier name.
    private static bool SkipName(ReadOnlySpan<byte> message, ref int at)
    {
        while (at < message.Length)
        {
            var length = message[at];
            if (length == 0)
            {
                at += 1;
                return true;
            }

            if (length >= 0xC0)
            {
                at += 2;
                return at <= message.Length;
            }

            if (length > 63)
            {
                return false;
            }

            at += 1 + length;
        }

        return false;
    }

    // Length-prefixed "key=value" strings; keys ignore case and the first of a repeated key wins (RFC 6763 §6).
    private static Dictionary<string, string>? ReadTxt(ReadOnlySpan<byte> data)
    {
        var txt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at < data.Length;)
        {
            var end = at + 1 + data[at];
            if (end > data.Length)
            {
                return null;
            }

            var entry = Encoding.UTF8.GetString(data[(at + 1)..end]);
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                txt.TryAdd(entry[..equals], entry[(equals + 1)..]);
            }

            at = end;
        }

        return txt;
    }

    private static DiscoveredPrinter? ToPrinter(Dictionary<string, string> txt, IPAddress sender, DateTimeOffset now)
    {
        if (!txt.TryGetValue("sn", out var serial) || string.IsNullOrWhiteSpace(serial)
            || !txt.TryGetValue("machine_type", out var machineType)
            || PrinterModelCatalog.TryResolveSlicedModel(machineType) is not { Brand: PrinterBrand.Snapmaker } model)
        {
            return null;
        }

        serial = serial.Trim();
        var name = txt.TryGetValue("device_name", out var deviceName) && !string.IsNullOrWhiteSpace(deviceName) ? deviceName.Trim() : serial;

        // The packet's source, reachable from here by definition; TXT ip is what the printer thinks its address is.
        return new DiscoveredPrinter(serial, name, model.Brand.ToString(), model.ModelCode, machineType.Trim(), sender.ToString(), now);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Snapmaker discovery can't share UDP {Port}; querying from an ephemeral port, so boot announcements are missed.")]
    private partial void LogSharedPortUnavailable(Exception exception, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Snapmaker discovery could not open a UDP socket; Snapmaker printers won't be discovered.")]
    private partial void LogSocketFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapmaker discovery query failed; the next one retries.")]
    private partial void LogQueryFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapmaker discovery receive failed; continuing.")]
    private partial void LogReceiveFailed(Exception exception);
}
