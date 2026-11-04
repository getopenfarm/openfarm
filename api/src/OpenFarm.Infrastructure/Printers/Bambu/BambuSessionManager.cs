using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>Owns shared, reference-counted sessions. All users, including verification, wait for SUBACK.</summary>
internal sealed partial class BambuSessionManager
{
    internal static BambuSessionManager Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly Dictionary<string, Task> _closing = [];

    private static string Key(PrinterConnectionDetails details, BambuTransportOptions options) =>
        $"{details.IpAddress}:{options.MqttPort}/{details.SerialNumber}/{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(details.AccessCode)))}";

    internal Lease Acquire(PrinterConnectionDetails details, BambuTransportOptions options, ILogger logger)
    {
        var key = Key(details, options);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var session))
            {
                session = new Session(details, options, logger, _closing.GetValueOrDefault(key, Task.CompletedTask));
                _sessions.Add(key, session);
            }
            var lease = new Lease(this, key, session, session.Count > 0);
            session.Add(lease);
            session.Start();
            return lease;
        }
    }

    /// <summary>
    /// Tells the printer's open session that the printer was heard from outside MQTT. A P1S stops pushing reports while it
    /// receives a file, and a slow upload would otherwise outlast the silence that marks it offline. No session, no effect.
    /// </summary>
    internal void ReportAlive(PrinterConnectionDetails details, BambuTransportOptions options)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(Key(details, options), out var session)) session.Broadcast(new(Alive: true));
        }
    }

    private async ValueTask ReleaseAsync(Lease lease)
    {
        Task? stopped = null;
        lock (_gate)
        {
            lease.Session.Remove(lease);
            if (lease.Session.Count == 0)
            {
                _sessions.Remove(lease.Key);
                stopped = lease.Session.DisposeAsync().AsTask();
                _closing[lease.Key] = stopped;
            }
        }
        if (stopped is not null)
        {
            try { await stopped; }
            finally
            {
                lock (_gate)
                {
                    if (_closing.GetValueOrDefault(lease.Key) == stopped) _closing.Remove(lease.Key);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT report subscription ready for printer {Serial}.")]
    private static partial void LogReady(ILogger logger, string serial);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {Serial} certificate validation failed ({Reason}); using unverified TLS compatibility mode for this session.")]
    private static partial void LogFallback(ILogger logger, string serial, string reason);
    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT session for printer {Serial} failed.")]
    private static partial void LogFailed(ILogger logger, string serial, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "MQTT cleanup for {Serial} failed.")]
    private static partial void LogCleanup(ILogger logger, string serial, Exception exception);

    internal sealed record Message(string? Payload = null, bool Started = false, bool Ended = false, bool Alive = false)
    {
        internal DateTimeOffset ReceivedAtUtc { get; } = DateTimeOffset.UtcNow;
    }

    internal sealed class Lease(BambuSessionManager owner, string key, Session session, bool refreshRequired) : IAsyncDisposable
    {
        private int _disposed;
        internal bool RefreshRequired => refreshRequired;
        internal string Key => key;
        internal Session Session => session;
        internal Channel<Message> Messages { get; } = Channel.CreateUnbounded<Message>();
        internal Task ReadyAsync(CancellationToken ct) => session.ReadyAsync(ct);
        internal Task PublishAsync(string payload, bool command, CancellationToken ct) => session.PublishAsync(payload, command, ct);
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0 ? owner.ReleaseAsync(this) : ValueTask.CompletedTask;
    }

    internal sealed class Session(PrinterConnectionDetails details, BambuTransportOptions options, ILogger logger, Task previousClosed) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly List<Lease> _leases = [];
        private readonly CancellationTokenSource _stop = new();
        private TaskCompletionSource<IMqttClient> _ready = NewReady();
        private Task? _loop;
        private bool _insecure;
        internal int Count => _leases.Count; // Manager gate protects membership.
        private static TaskCompletionSource<IMqttClient> NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Add(Lease lease)
        {
            lock (_gate)
            {
                _leases.Add(lease);
                if (_ready.Task.IsCompletedSuccessfully) lease.Messages.Writer.TryWrite(new(Started: true));
            }
        }
        internal void Remove(Lease lease)
        {
            lock (_gate) _leases.Remove(lease);
            lease.Messages.Writer.TryComplete();
        }
        internal void Start() => _loop ??= RunAsync();
        internal void Broadcast(Message message)
        {
            lock (_gate) foreach (var lease in _leases) lease.Messages.Writer.TryWrite(message);
        }
        internal async Task ReadyAsync(CancellationToken ct)
        {
            Task<IMqttClient> ready;
            lock (_gate) ready = _ready.Task;
            await ready.WaitAsync(ct);
        }
        internal async Task PublishAsync(string payload, bool command, CancellationToken ct)
        {
            Task<IMqttClient> ready;
            lock (_gate) ready = _ready.Task;
            var client = await ready.WaitAsync(ct);
            try
            {
                // QoS 0: a P1S (fw 01.10) runs a QoS 1 command at once but sends no PUBACK, so waiting for one failed every
                // start and cancel. The printer's reply on the report topic is the acknowledgement; telemetry settles a start.
                await client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic($"device/{details.SerialNumber}/request").WithPayload(payload).Build(), ct);
            }
            catch (Exception ex) when (command && payload.Contains("\"command\":\"project_file\"", StringComparison.Ordinal))
            {
                throw new BambuStartUnconfirmedException(ex);
            }
        }

        private async Task RunAsync()
        {
            var failures = 0;
            try
            {
                await previousClosed.WaitAsync(_stop.Token);
                while (!_stop.IsCancellationRequested)
                {
                    using var client = options.CreateMqttClient();
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    string? certificateFailure = null;
                    client.ApplicationMessageReceivedAsync += e =>
                    {
                        if (e.ApplicationMessage.Topic == $"device/{details.SerialNumber}/report")
                            Broadcast(new(Encoding.UTF8.GetString(e.ApplicationMessage.Payload)));
                        return Task.CompletedTask;
                    };
                    client.DisconnectedAsync += _ =>
                    {
                        lock (_gate)
                        {
                            if (_ready.Task.IsCompletedSuccessfully && ReferenceEquals(_ready.Task.Result, client))
                                _ready = NewReady();
                        }
                        closed.TrySetResult();
                        return Task.CompletedTask;
                    };
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        attempt.CancelAfter(options.ConnectTimeout);
                        var mqttOptions = BambuLanConnector.BuildClientOptions(details, options,
                            certificate =>
                            {
                                if (_insecure) return true;
                                var accepted = options.ValidateCertificate(certificate);
                                if (!accepted) certificateFailure = BambuCertificateTrust.CompatibilityFailure(certificate, details.SerialNumber);
                                return accepted && (!options.CheckCertificateSerial || BambuCertificateTrust.MatchesSerial(certificate, details.SerialNumber));
                            });
                        await client.ConnectAsync(mqttOptions, attempt.Token);
                        var subscription = await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                            .WithTopicFilter($"device/{details.SerialNumber}/report").Build(), attempt.Token);
                        if (subscription.Items.Count == 0 || subscription.Items.Any(i => i.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2))
                            throw new IOException("Printer refused the report subscription.");
                        Broadcast(new(Started: true));
                        failures = 0;
                        LogReady(logger, details.SerialNumber);
                        foreach (var payload in new[] { """{"pushing":{"sequence_id":"0","command":"pushall"}}""", """{"info":{"sequence_id":"0","command":"get_version"}}""" })
                            await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"device/{details.SerialNumber}/request").WithPayload(payload).Build(), attempt.Token);
                        if (!client.IsConnected) throw new IOException("Printer disconnected during initialization.");
                        lock (_gate) _ready.TrySetResult(client);
                        await closed.Task.WaitAsync(_stop.Token);
                    }
                    catch (Exception ex) when (!_stop.IsCancellationRequested)
                    {
                        if (!_insecure && options.AllowCertificateFallback && certificateFailure is not null)
                        {
                            _insecure = true;
                            LogFallback(logger, details.SerialNumber, certificateFailure);
                        }
                        LogFailed(logger, details.SerialNumber, ex);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (_ready.Task.IsCompleted) _ready = NewReady();
                        }
                        Broadcast(new(Ended: true));
                        if (client.IsConnected)
                        {
                            using var cleanup = new CancellationTokenSource(options.ConnectTimeout);
                            try { await client.DisconnectAsync(cancellationToken: cleanup.Token); }
                            catch (Exception ex) { LogCleanup(logger, details.SerialNumber, ex); }
                        }
                    }
                    var delay = TimeSpan.FromMilliseconds(Math.Min(options.ReconnectDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failures++, 16)), options.MaxReconnectDelay.TotalMilliseconds));
                    await Task.Delay(delay, _stop.Token);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { /* MQTTnet can wrap lifecycle cancellation as a connecting failure. */ }
            finally
            {
                lock (_gate) _ready.TrySetCanceled(_stop.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            if (_loop is not null) await _loop;
            _stop.Dispose();
        }
    }
}
