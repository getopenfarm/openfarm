using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Tests.Printers.Snapmaker;

/// <summary>
/// Deliberately small Moonraker peer on a loopback port, not a firmware emulator: <c>/machine/system_info</c>, gcode file
/// downloads, and a websocket answering the JSON-RPC calls the U1 connector makes, plus pushed notifications and forced
/// socket drops. Request/answer shapes follow Snapmaker's open-source <c>u1-moonraker</c>.
/// </summary>
internal sealed class MoonrakerHarness : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Peer> _peers = new();
    private readonly Channel<(string Method, JsonNode? Params)> _requests = Channel.CreateUnbounded<(string, JsonNode?)>();
    private readonly Task _acceptLoop;
    private int _nextPeer;
    private int _subscribeCount;

    public MoonrakerHarness(string serialNumber = "U1TEST0001")
    {
        Port = FreeLoopbackPort();
        SerialNumber = serialNumber;
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port { get; }

    /// <summary>What <c>/machine/system_info</c> reports; change it to put another printer at this address.</summary>
    public string SerialNumber { get; set; }

    public string MachineType { get; set; } = "Snapmaker U1";

    /// <summary>When set, HTTP needs this <c>X-Api-Key</c> and a websocket must identify with it before anything else.</summary>
    public string? RequiredApiKey { get; init; }

    /// <summary>The <c>status</c> answered to <c>printer.objects.subscribe</c>.</summary>
    public JsonObject Status { get; set; } = MoonrakerStatusMapperTests.Fixture("snapmaker-u1-idle.json");

    public JsonArray Exceptions { get; set; } = [];

    /// <summary>Files under the gcodes root, by path.</summary>
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    /// <summary>Raw multipart bodies posted to <c>/server/files/upload</c>.</summary>
    public ConcurrentQueue<string> Uploads { get; } = new();

    /// <summary>Paths of every other POST (<c>/printer/print/pause</c>, ...).</summary>
    public ConcurrentQueue<string> Commands { get; } = new();

    /// <summary>What <c>/server/files/camera/monitor.jpg</c> serves; null is 404, like a camera that hasn't woken yet.</summary>
    public byte[]? CameraFrame { get; set; }

    /// <summary>What <c>server.files.start_local_print</c> answers; null answers "method not found", like firmware before 1.3.</summary>
    public JsonObject? StartResult { get; set; } = new() { ["state"] = "success", ["message"] = "Print started" };

    public int SubscribeCount => Volatile.Read(ref _subscribeCount);

    public int ActiveSockets => _peers.Count;

    public PrinterConnectionDetails Details(string? serialNumber = null, string apiKey = "") =>
        new("Snapmaker", "U1", "127.0.0.1", serialNumber ?? SerialNumber, apiKey);

    public MoonrakerTransportOptions Options => new()
    {
        Port = Port,
        RequestTimeout = TimeSpan.FromSeconds(2),
        CommandTimeout = TimeSpan.FromSeconds(2),
        ReconnectDelay = TimeSpan.FromMilliseconds(30),
        KeepAlive = TimeSpan.FromSeconds(1),
        TelemetryInterval = TimeSpan.FromMilliseconds(40),
        TelemetryFloor = TimeSpan.FromMilliseconds(200),
    };

    public SnapmakerU1Connector CreateConnector(MoonrakerTransportOptions? options = null) =>
        new(NullLogger<SnapmakerU1Connector>.Instance, options ?? Options);

    public Task<(string Method, JsonNode? Params)> NextRequestAsync(string method) => NextRequestCoreAsync(method);

    public Task PushStatusAsync(string deltaJson) => NotifyAsync("notify_status_update", $"[{deltaJson}, 1.0]");

    public async Task NotifyAsync(string method, string paramsJson)
    {
        var frame = $$"""{"jsonrpc":"2.0","method":"{{method}}","params":{{paramsJson}}}""";
        foreach (var peer in _peers.Values.Where(p => p.Subscribed))
            await peer.SendAsync(frame);
    }

    public void DropConnections()
    {
        foreach (var peer in _peers.Values)
        {
            peer.Socket.Abort();
            // Close the TCP connection too, so the client sees the drop now rather than at its keepalive deadline.
            peer.Context.Response.Abort();
        }
    }

    private async Task<(string Method, JsonNode? Params)> NextRequestCoreAsync(string method)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            var request = await _requests.Reader.ReadAsync(timeout.Token);
            if (request.Method == method) return request;
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var path = Uri.UnescapeDataString(request.Url!.AbsolutePath);
        if (request.IsWebSocketRequest && path == "/websocket")
        {
            await RunPeerAsync(context);
            return;
        }

        using var response = context.Response;
        if (RequiredApiKey is not null && request.Headers["X-Api-Key"] != RequiredApiKey)
        {
            response.StatusCode = 401;
            return;
        }

        const string gcodes = "/server/files/gcodes/";
        if (request.HttpMethod == "POST")
        {
            if (path == "/server/files/upload")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                Uploads.Enqueue(await reader.ReadToEndAsync());
            }
            else
            {
                Commands.Enqueue(path);
            }

            await WriteAsync(response, """{"result":"ok"}"""u8.ToArray());
        }
        else if (path == "/server/files/camera/monitor.jpg" && CameraFrame is { } frame)
        {
            await WriteAsync(response, frame);
        }
        else if (path == "/machine/system_info")
        {
            var productInfo = new JsonObject
            {
                ["machine_type"] = MachineType,
                ["serial_number"] = SerialNumber,
                ["nozzle_diameter"] = new JsonArray(0.4, 0.4, 0.4, 0.4),
            };
            var body = new JsonObject { ["result"] = new JsonObject { ["system_info"] = new JsonObject { ["product_info"] = productInfo } } };
            await WriteAsync(response, Encoding.UTF8.GetBytes(body.ToJsonString()));
        }
        else if (path.StartsWith(gcodes, StringComparison.Ordinal) && Files.TryGetValue(path[gcodes.Length..], out var file))
            await WriteAsync(response, file);
        else
            response.StatusCode = 404;
    }

    private async Task RunPeerAsync(HttpListenerContext context)
    {
        var webSocket = (await context.AcceptWebSocketAsync(subProtocol: null)).WebSocket;
        var id = Interlocked.Increment(ref _nextPeer);
        var peer = new Peer(context, webSocket) { Identified = RequiredApiKey is null };
        _peers[id] = peer;
        try
        {
            while (await ReceiveAsync(webSocket) is { } message)
            {
                var frame = JsonNode.Parse(message)!.AsObject();
                var method = frame["method"]!.GetValue<string>();
                var parameters = frame["params"]?.DeepClone();
                _requests.Writer.TryWrite((method, parameters));
                var (result, error) = Answer(peer, method, parameters);
                var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = frame["id"]?.DeepClone() };
                if (error is null) reply["result"] = result;
                else reply["error"] = error;
                await peer.SendAsync(reply.ToJsonString());
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Dropped on purpose by a test, or the client went away.
        }
        finally
        {
            _peers.TryRemove(id, out _);
            peer.Dispose();
        }
    }

    private (JsonNode? Result, JsonNode? Error) Answer(Peer peer, string method, JsonNode? parameters)
    {
        if (method == "server.connection.identify")
        {
            if (RequiredApiKey is not null && parameters?["api_key"]?.GetValue<string>() != RequiredApiKey)
                return (null, new JsonObject { ["code"] = 401, ["message"] = "Unauthorized" });
            peer.Identified = true;
            return (new JsonObject { ["connection_id"] = 1 }, null);
        }

        if (!peer.Identified) return (null, new JsonObject { ["code"] = 401, ["message"] = "Unauthorized" });
        switch (method)
        {
            case "printer.objects.subscribe":
                Interlocked.Increment(ref _subscribeCount);
                peer.Subscribed = true;
                return (new JsonObject { ["eventtime"] = 1.0, ["status"] = Status.DeepClone() }, null);
            case "server.exception.query":
                return (new JsonObject { ["state"] = "success", ["exceptions"] = Exceptions.DeepClone() }, null);
            case "server.files.start_local_print" when StartResult is not null:
                return (StartResult.DeepClone(), null);
            case "camera.start_monitor":
                return (new JsonObject { ["state"] = "success" }, null);
            default:
                return (null, new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" });
        }
    }

    private async Task<string?> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, _stop.Token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(message.ToArray());
        }
    }

    private static async Task WriteAsync(HttpListenerResponse response, byte[] body)
    {
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body);
    }

    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        DropConnections();
        _listener.Close();
        await _acceptLoop;
        _stop.Dispose();
    }

    private sealed class Peer(HttpListenerContext context, WebSocket socket) : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public HttpListenerContext Context { get; } = context;

        public WebSocket Socket { get; } = socket;

        public bool Identified { get; set; }

        public bool Subscribed { get; set; }

        public async Task SendAsync(string json)
        {
            await _sendLock.WaitAsync();
            try
            {
                await Socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            Socket.Dispose();
            _sendLock.Dispose();
        }
    }
}
