using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Passively listens for the SSDP <c>NOTIFY</c> packets Bambu printers send every few seconds — both
/// as a broadcast to UDP 2021 and multicast to 239.255.255.250:1990 — and keeps the most recent
/// sighting per serial. Needs the host to be on the printers' LAN — a bridged Docker container (or
/// Docker Desktop) never hears these packets.
/// </summary>
public sealed partial class BambuSsdpDiscoveryService(ILogger<BambuSsdpDiscoveryService> logger)
    : BackgroundService, IPrinterDiscovery
{
    public const int BroadcastPort = 2021;
    public const int MulticastPort = 1990;
    private static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.255.250");
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DiscoveredPrinter> _seen = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DiscoveredPrinter> GetRecent()
    {
        var cutoff = DateTimeOffset.UtcNow - StaleAfter;
        return _seen.Values.Where(p => p.LastSeenAt >= cutoff).ToList();
    }

    // Two independent listeners: Bambu Studio/Orca bind 2021 exclusively, so on a workstation that
    // runs a slicer only the shareable multicast port is available. Either one alone is enough.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            ListenAsync(BroadcastPort, multicast: false, stoppingToken),
            ListenAsync(MulticastPort, multicast: true, stoppingToken));

    private async Task ListenAsync(int port, bool multicast, CancellationToken stoppingToken)
    {
        using var udp = new UdpClient { ExclusiveAddressUse = false };
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            if (multicast)
            {
                udp.JoinMulticastGroup(MulticastGroup);
            }
        }
        catch (SocketException ex)
        {
            LogBindFailed(ex, port);
            return;
        }

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

            var printer = Parse(Encoding.UTF8.GetString(packet.Buffer), packet.RemoteEndPoint.Address, DateTimeOffset.UtcNow);
            if (printer is not null)
            {
                _seen[printer.SerialNumber] = printer;
            }
        }
    }

    /// <summary>Parses one NOTIFY payload; null when it isn't a Bambu printer announcement.</summary>
    public static DiscoveredPrinter? Parse(string payload, IPAddress sender, DateTimeOffset now)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in payload.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        if (!headers.TryGetValue("USN", out var serial) || string.IsNullOrWhiteSpace(serial)
            || !headers.TryGetValue("DevModel.bambu.com", out var rawModel))
        {
            return null;
        }

        // Location is a bare IP on Bambu firmware; fall back to the packet's sender otherwise.
        var ip = headers.TryGetValue("Location", out var location) && IPAddress.TryParse(location, out var parsed)
            ? parsed
            : sender;

        var name = headers.TryGetValue("DevName.bambu.com", out var devName) && devName.Length > 0 ? devName : serial;

        return new DiscoveredPrinter(serial, name, PrinterModelCatalog.TryMapBambuDeviceModel(rawModel), rawModel, ip.ToString(), now);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer discovery could not listen on UDP {Port}; relying on the other discovery port.")]
    private partial void LogBindFailed(Exception exception, int port);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Printer discovery receive failed; continuing.")]
    private partial void LogReceiveFailed(Exception exception);
}
