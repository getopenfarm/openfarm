using System.Collections.Concurrent;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

/// <summary>
/// Deliberately small MQTT 3.1.1 wire peer, not a general broker or a firmware emulator.
/// Supports CONNECT, QoS-0 PUBLISH, SUBSCRIBE, PING and DISCONNECT used by the LAN connector.
/// Scripted reports and forced socket closure exercise MQTTnet itself over real TLS sockets.
/// </summary>
internal sealed class BambuMqttHarness : IAsyncDisposable
{
    private readonly LoopbackTlsServer _server;
    private readonly ConcurrentDictionary<int, Session> _sessions = new();
    private readonly Channel<Request> _requests = Channel.CreateUnbounded<Request>();
    private int _connectionCount;
    private int _subscriptionCount;
    private int _activeConnections;

    public BambuMqttHarness(string serialNumber = "TEST-P1S")
    {
        Details = new PrinterConnectionDetails("Bambu", "P1S", "127.0.0.1", serialNumber, "test-code");
        _server = new LoopbackTlsServer(HandleSessionAsync);
    }

    public PrinterConnectionDetails Details { get; }
    public bool ReplyToVersion { get; init; } = true;
    public bool ReplyToPushAll { get; init; } = true;
    public bool WithholdFirstConnack { get; init; }
    public string InitialReport { get; init; } = """{"print":{"gcode_state":"IDLE","nozzle_temper":25,"bed_temper":24,"mc_percent":0}}""";
    public int ConnectionCount => Volatile.Read(ref _connectionCount);
    public int SubscriptionCount => Volatile.Read(ref _subscriptionCount);
    public int ActiveConnections => Volatile.Read(ref _activeConnections);
    public ConcurrentQueue<(string Protocol, byte Version, string Username)> Handshakes { get; } = new();
    public ConcurrentQueue<string> Subscriptions { get; } = new();

    public BambuTransportOptions Options => new()
    {
        MqttPort = _server.Port,
        ValidateCertificate = _server.TrustCertificate,
        VerifyTimeout = TimeSpan.FromSeconds(2),
        CommandTimeout = TimeSpan.FromSeconds(2),
        ReconnectDelay = TimeSpan.FromMilliseconds(30),
    };

    public BambuLanConnector CreateConnector(BambuTransportOptions? options = null) =>
        new(NullLogger<BambuLanConnector>.Instance, options ?? Options);

    public Task<Request> NextRequestAsync() => _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

    public async Task PublishReportAsync(string payload, string? topic = null)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Subscribed)
            {
                await session.PublishAsync(topic ?? ReportTopic, payload, CancellationToken.None);
            }
        }
    }

    public void DropConnections()
    {
        foreach (var session in _sessions.Values)
        {
            session.Stream.Dispose();
        }
    }

    private string ReportTopic => $"device/{Details.SerialNumber}/report";

    private async Task HandleSessionAsync(SslStream stream, CancellationToken cancellationToken)
    {
        var (header, body) = await ReadPacketAsync(stream, cancellationToken);
        if (header != 0x10)
        {
            throw new InvalidDataException("Expected MQTT CONNECT.");
        }

        var offset = 0;
        var protocol = ReadString(body, ref offset);
        var version = body[offset++];
        var flags = body[offset++];
        offset += 2; // Keepalive.
        _ = ReadString(body, ref offset); // Client ID.
        var username = (flags & 0x80) != 0 ? ReadString(body, ref offset) : "";
        var password = (flags & 0x40) != 0 ? ReadString(body, ref offset) : "";
        Handshakes.Enqueue((protocol, version, username));
        var accepted = protocol == "MQTT" && version == 4 && username == "bblp" && password == Details.AccessCode;
        var id = Interlocked.Increment(ref _connectionCount);
        if (WithholdFirstConnack && id == 1)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        using var session = new Session(stream);
        await session.SendAsync(0x20, [0, accepted ? (byte)0 : (byte)4], cancellationToken);
        if (!accepted)
        {
            return;
        }

        _sessions[id] = session;
        Interlocked.Increment(ref _activeConnections);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (header, body) = await ReadPacketAsync(stream, cancellationToken);
                switch (header >> 4)
                {
                    case 8:
                        offset = 2; // Packet identifier.
                        var topic = ReadString(body, ref offset);
                        Subscriptions.Enqueue(topic);
                        session.Subscribed = topic == ReportTopic;
                        await session.SendAsync(0x90, [body[0], body[1], 0], cancellationToken);
                        Interlocked.Increment(ref _subscriptionCount);
                        break;
                    case 3:
                        offset = 0;
                        var requestTopic = ReadString(body, ref offset);
                        var payload = Encoding.UTF8.GetString(body, offset, body.Length - offset);
                        _requests.Writer.TryWrite(new Request(id, requestTopic, payload));
                        using (var document = JsonDocument.Parse(payload))
                        {
                            if (ReplyToVersion && document.RootElement.TryGetProperty("info", out _))
                            {
                                await session.PublishAsync(ReportTopic, """{"info":{"command":"get_version"}}""", cancellationToken);
                            }

                            if (ReplyToPushAll && document.RootElement.TryGetProperty("pushing", out _))
                            {
                                await session.PublishAsync(ReportTopic, InitialReport, cancellationToken);
                            }
                        }

                        break;
                    case 12:
                        await session.SendAsync(0xD0, [], cancellationToken);
                        break;
                    case 14:
                        return;
                    default:
                        throw new InvalidDataException($"Unsupported MQTT packet {header:X2}.");
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // DropConnections is a deliberate fault-injection operation.
        }
        finally
        {
            _sessions.TryRemove(id, out _);
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    private static async Task<(byte Header, byte[] Body)> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var single = new byte[1];
        await stream.ReadExactlyAsync(single, cancellationToken);
        var header = single[0];
        var length = 0;
        var multiplier = 1;
        for (var i = 0; i < 4; i++)
        {
            await stream.ReadExactlyAsync(single, cancellationToken);
            length += (single[0] & 0x7F) * multiplier;
            if ((single[0] & 0x80) == 0)
            {
                if (length > 1024 * 1024)
                {
                    throw new InvalidDataException("Test packet exceeds size limit.");
                }

                var body = new byte[length];
                await stream.ReadExactlyAsync(body, cancellationToken);
                return (header, body);
            }

            multiplier *= 128;
        }

        throw new InvalidDataException("Invalid MQTT remaining length.");
    }

    private static string ReadString(byte[] body, ref int offset)
    {
        var length = (body[offset] << 8) | body[offset + 1];
        offset += 2;
        var value = Encoding.UTF8.GetString(body, offset, length);
        offset += length;
        return value;
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        _requests.Writer.TryComplete();
    }

    internal sealed record Request(int ConnectionId, string Topic, string Payload);

    private sealed class Session(SslStream stream) : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1);
        private int _subscribed;
        public SslStream Stream { get; } = stream;
        public bool Subscribed
        {
            get => Volatile.Read(ref _subscribed) != 0;
            set => Volatile.Write(ref _subscribed, value ? 1 : 0);
        }

        public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken)
        {
            var topicBytes = Encoding.UTF8.GetBytes(topic);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            byte[] body = [(byte)(topicBytes.Length >> 8), (byte)topicBytes.Length, .. topicBytes, .. payloadBytes];
            return SendAsync(0x30, body, cancellationToken);
        }

        public async Task SendAsync(byte header, byte[] body, CancellationToken cancellationToken)
        {
            var packet = new List<byte> { header };
            var length = body.Length;
            do
            {
                var digit = (byte)(length % 128);
                length /= 128;
                packet.Add(length > 0 ? (byte)(digit | 0x80) : digit);
            }
            while (length > 0);
            packet.AddRange(body);
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await Stream.WriteAsync(packet.ToArray(), cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose() => _writeLock.Dispose();
    }
}
