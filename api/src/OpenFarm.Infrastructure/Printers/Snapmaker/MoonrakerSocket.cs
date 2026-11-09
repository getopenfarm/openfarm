using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>
/// Instance-local Infrastructure test seams for the Snapmaker connector. Production constructors always use the LAN
/// defaults; these settings are deliberately not exposed through configuration or printer registration.
/// </summary>
internal sealed record MoonrakerTransportOptions
{
    // ponytail: fixed Moonraker port; add a printer field when someone runs it elsewhere.
    public int Port { get; init; } = 7125;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(120);
    public int CameraPort { get; init; } = 80;
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(300);
    public TimeSpan UploadTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan TelemetryInterval { get; init; } = TimeSpan.FromSeconds(1);
    // A liveness-only event at least this often while the printer keeps sending frames.
    public TimeSpan AliveInterval { get; init; } = TimeSpan.FromSeconds(10);
    // ponytail: fixed 1 Hz camera poll; the U1's snapshot refreshes about that often.
    public TimeSpan CameraPollInterval { get; init; } = TimeSpan.FromSeconds(1);
    // The camera idles a few seconds after the last camera.start_monitor.
    public TimeSpan CameraWakeInterval { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>A JSON-RPC error answer from Moonraker (unknown method, Klippy not ready, a refused command).</summary>
internal sealed class MoonrakerRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// One Moonraker websocket: JSON-RPC 2.0 requests matched to their answers by id, and server notifications
/// (<c>notify_status_update</c>, ...) queued for the reader. Authenticates with query-token authentication
/// and identifies OpenFarm with server.connection.identify.
/// </summary>
internal sealed class MoonrakerSocket : IAsyncDisposable
{
    private static readonly JsonSerializerOptions IgnoreNullOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly Channel<JsonObject> _notifications = Channel.CreateUnbounded<JsonObject>();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _receiveLoop;
    private int _nextId;
    private long _lastReceivedTicks = DateTimeOffset.UtcNow.UtcTicks;

    /// <summary>When the printer last sent anything (an answer or a notification); Moonraker pushes status only on change.</summary>
    public DateTimeOffset LastReceivedUtc => new(Interlocked.Read(ref _lastReceivedTicks), TimeSpan.Zero);

    private MoonrakerSocket(ClientWebSocket socket)
    {
        _socket = socket;
        _receiveLoop = ReceiveLoopAsync();
    }

    /// <summary>Server notifications in arrival order; completes when the socket closes.</summary>
    public ChannelReader<JsonObject> Notifications => _notifications.Reader;

    public static async Task<MoonrakerSocket> ConnectAsync(Uri baseUri, string? apiKey, MoonrakerTransportOptions options, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        // PING/PONG with a deadline, so a printer that vanishes closes the socket instead of leaving it silently open.
        socket.Options.KeepAliveInterval = options.KeepAlive;
        socket.Options.KeepAliveTimeout = options.KeepAlive;
        var uri = new UriBuilder(baseUri) { Scheme = "ws", Path = "/websocket", Query = string.IsNullOrWhiteSpace(apiKey) ? "" : "token=" + Uri.EscapeDataString(apiKey) }.Uri;
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(options.RequestTimeout);
        try
        {
            await socket.ConnectAsync(uri, connectCts.Token);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        var connection = new MoonrakerSocket(socket);
        try
        {
            await connection.CallAsync("server.connection.identify", new
            {
                client_name = "OpenFarm",
                version = "1.0.0",
                type = "other",
                url = "https://github.com/getopenfarm/openfarm",

            }, options.RequestTimeout, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<JsonNode?> CallAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken, Action? sent = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var answer = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = answer;
        try
        {
            var request = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", method, @params = parameters, id }, IgnoreNullOptions);
            using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            callCts.CancelAfter(timeout);
            await _sendLock.WaitAsync(callCts.Token);
            try
            {
                await _socket.SendAsync(request, WebSocketMessageType.Text, endOfMessage: true, callCts.Token);
                sent?.Invoke();
            }
            finally
            {
                _sendLock.Release();
            }

            return await answer.Task.WaitAsync(callCts.Token);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private static readonly string[] PrintStateFields = ["state"];
    private event Action<string>? PrintStateObserved;
    private string? _printState;

    internal async Task CallWithStateAckAsync(string method, TimeSpan timeout, bool subscribe, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool Matches(string? state) => method switch
        {
            "printer.print.pause" => state == "paused",
            "printer.print.resume" => state == "printing",
            _ => state is "cancelled" or "standby" or "complete",
        };
        void Observe(string state) { if (Matches(state)) observed.TrySetResult(); }
        PrintStateObserved += Observe;
        try
        {
            if (_printState is null && subscribe)
                await CallAsync("printer.objects.subscribe", new { objects = new { print_stats = PrintStateFields } }, timeout, deadline.Token);
            if (Matches(_printState)) observed.TrySetResult();
            var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var rpc = CallAsync(method, null, timeout, deadline.Token, () => sent.TrySetResult());
            _ = rpc.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            // A state already reached still waits for the command to go out, so the caller never closes the socket on it.
            var completed = await Task.WhenAny(rpc, Task.WhenAll(sent.Task, observed.Task)).WaitAsync(deadline.Token);
            await completed;
        }
        finally { PrintStateObserved -= Observe; }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, _closing.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                Interlocked.Exchange(ref _lastReceivedTicks, DateTimeOffset.UtcNow.UtcTicks);
                Dispatch(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                message.SetLength(0);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The socket closed under us; readers see the completed channel and the failed requests below.
        }
        finally
        {
            _notifications.Writer.TryComplete();
            foreach (var pending in _pending.Values)
                pending.TrySetException(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
        }
    }

    private void Dispatch(string json)
    {
        JsonObject? frame;
        try
        {
            frame = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }

        if (frame is null) return;
        if (frame["method"]?.ToString() is "notify_klippy_disconnected" or "notify_klippy_shutdown") _printState = null;
        var status = frame["result"] is JsonObject resultObject ? resultObject["status"] : null;
        if (frame["method"]?.ToString() == "notify_status_update" && frame["params"] is JsonArray { Count: > 0 } parameters) status = parameters[0];
        if (status is JsonObject statusObject && statusObject["print_stats"] is JsonObject stats && stats["state"] is JsonValue state && state.TryGetValue<string>(out var word))
        {
            _printState = word;
            PrintStateObserved?.Invoke(word);
        }
        if (frame["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var id))
        {
            if (!_pending.TryGetValue(id, out var answer)) return;
            if (frame["error"] is JsonObject error)
                answer.TrySetException(new MoonrakerRpcException(
                    error["code"] is JsonValue code && code.TryGetValue<int>(out var c) ? c : 0,
                    error["message"]?.ToString() ?? "Moonraker request failed."));
            else
                answer.TrySetResult(frame["result"]?.DeepClone());
            return;
        }

        if (frame["method"] is not null) _notifications.Writer.TryWrite(frame);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeCts.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // Best effort; the printer may already be gone.
        }

        await _closing.CancelAsync();
        await _receiveLoop;
        _socket.Dispose();
        _sendLock.Dispose();
        _closing.Dispose();
    }
}
