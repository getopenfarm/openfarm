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
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan UploadTimeout { get; init; } = TimeSpan.FromMinutes(5);
    // The firmware answers start_local_print once its pre-print checks pass.
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan TelemetryInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan TelemetryFloor { get; init; } = TimeSpan.FromSeconds(10);
    // ponytail: fixed 1 Hz camera poll; the U1's snapshot refreshes about that often.
    public TimeSpan CameraPollInterval { get; init; } = TimeSpan.FromSeconds(1);
    // The camera idles a few seconds after the last camera.start_monitor.
    public TimeSpan CameraWakeInterval { get; init; } = TimeSpan.FromSeconds(8);
}

/// <summary>A JSON-RPC error answer from Moonraker (unknown method, Klippy not ready, a refused command).</summary>
internal sealed class MoonrakerRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// One Moonraker websocket: JSON-RPC 2.0 requests matched to their answers by id, and server notifications
/// (<c>notify_status_update</c>, ...) queued for the reader. Authenticates after connecting with
/// <c>server.connection.identify</c>, Moonraker's documented way for a non-browser client to pass its API key.
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
        var uri = new UriBuilder(baseUri) { Scheme = "ws", Path = "/websocket" }.Uri;
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
                api_key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            }, options.RequestTimeout, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<JsonNode?> CallAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
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
