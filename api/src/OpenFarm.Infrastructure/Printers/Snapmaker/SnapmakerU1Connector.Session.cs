using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

public sealed partial class SnapmakerU1Connector
{
    /// <summary>
    /// Reconnects for as long as the connection is wanted. The state tracker outlives each websocket, so a Wi-Fi blip
    /// never re-sends a state the printer already reported — re-sending FINISH would undo an operator's Mark ready.
    /// </summary>
    private async Task RunSessionsAsync(PrinterConnectionDetails details, ChannelWriter<PrinterConnectorEvent> writer, CancellationToken cancellationToken)
    {
        U1SessionRegistry.Entry registration;
        try { registration = U1SessionRegistry.Register(details, _options); }
        catch (Exception ex) { writer.TryComplete(ex); return; }
        var tracker = new StateTracker();
        var failures = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var socket = await MoonrakerSocket.ConnectAsync(BaseUri(details), details.AccessCode, _options, cancellationToken);
                    // Moonraker messages carry no serial, so a DHCP swap would book another printer's telemetry here.
                    if (await IsSamePrinterAsync(details, cancellationToken))
                    {
                        failures = 0;
                        await RunSessionAsync(details, registration, socket, tracker, writer, cancellationToken);
                    }
                    else
                        LogSerialMismatch(_logger, details.IpAddress, details.SerialNumber);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    LogSessionFailed(_logger, details.IpAddress, ex);
                }

                registration.Reset();
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(_options.MaxReconnectDelay.TotalMilliseconds, _options.ReconnectDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failures++, 16)))), cancellationToken);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown; whatever a request was answering by then (a late error included) no longer matters.
        }
        finally
        {
            U1SessionRegistry.Remove(details, _options, registration);
            writer.TryComplete();
        }
    }

    private async Task RunSessionAsync(PrinterConnectionDetails details, U1SessionRegistry.Entry registration, MoonrakerSocket socket, StateTracker tracker, ChannelWriter<PrinterConnectorEvent> writer, CancellationToken cancellationToken)
    {
        var status = await SubscribeAsync(socket, cancellationToken);
        var filament = new U1FilamentState();
        filament.Merge(status);
        var alerts = new U1ExceptionTracker();
        alerts.SetActive(await QueryExceptionsAsync(socket, cancellationToken));
        var observedAt = DateTimeOffset.UtcNow;
        var aliveAt = observedAt;
        await using var poller = new U1FilamentPoller(details, _options, _logger, cancellationToken);
        using var workers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cameraWorker = KeepCameraAwakeAsync(socket, workers.Token);
        string? metadataFile = null;
        var lateAt = DateTimeOffset.UtcNow.AddMilliseconds(5500);
        writer.TryWrite(new PrinterConnectorEvent(observedAt, ConnectionStarted: true));
        var nozzlesDirty = true;
        void Publish(string? state)
        {
            var telemetry = MoonrakerStatusMapper.Telemetry(status, tracker.Current);
            var active = telemetry.ActiveSlots?.FirstOrDefault(v => v is not null)?.SlotPosition;
            telemetry = telemetry with { FeedReadings = filament.Read(active), NozzleDiametersMm = nozzlesDirty ? telemetry.NozzleDiametersMm : null };
            nozzlesDirty = false;
            writer.TryWrite(new PrinterConnectorEvent(observedAt, GcodeState: state, Telemetry: telemetry, HmsAlerts: alerts.Alerts));
        }
        try
        {
            Publish(tracker.Observe(status));
            registration.SetReady(socket);
            while (await WaitForNotificationsAsync(socket, cancellationToken))
            {
                var dirty = false;
                while (socket.Notifications.TryRead(out var notification))
                {
                    var method = notification["method"]?.ToString();
                    var payload = notification["params"] is JsonArray { Count: > 0 } parameters ? parameters[0] : null;
                    switch (method)
                    {
                        case "notify_status_update":
                            if (payload is JsonObject delta && delta.Any(p => p.Key.StartsWith("extruder", StringComparison.Ordinal) && p.Value is JsonObject fields && fields.ContainsKey("nozzle_diameter"))) nozzlesDirty = true;
                            Merge(status, payload);
                            filament.Merge(payload);
                            observedAt = DateTimeOffset.UtcNow;
                            alerts.Observe(status["print_stats"]?["state"]?.ToString());
                            dirty = true;
                            break;
                        case "notify_klippy_ready":
                            nozzlesDirty = true;
                            status = await SubscribeAsync(socket, cancellationToken);
                            filament.Merge(status);
                            observedAt = DateTimeOffset.UtcNow;
                            dirty = true;
                            break;
                        case "notify_klippy_shutdown" or "notify_klippy_disconnected":
                            status["webhooks"] = new JsonObject { ["state"] = "shutdown" };
                            observedAt = DateTimeOffset.UtcNow;
                            dirty = true;
                            break;
                        case "notify_exception_status":
                            alerts.SetActive(payload?["exceptions"]);
                            writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, HmsAlerts: alerts.Alerts));
                            break;
                        case "notify_exception_notification":
                            alerts.Notify(payload);
                            writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, HmsAlerts: alerts.Alerts));
                            break;
                        case "notify_filelist_changed":
                            if (payload?.ToJsonString().Contains("snapmaker/print_task.json", StringComparison.Ordinal) == true) poller.Refresh();
                            break;
                        case "notify_history_changed":
                            if (payload?["action"]?.ToString() == "finished" && payload["job"] is JsonObject job
                                && job["filename"]?.ToString() is { Length: > 0 } historyFile
                                && historyFile == status["print_stats"]?["filename"]?.ToString())
                            {
                                var terminal = job["status"]?.ToString() switch { "completed" => "complete", "cancelled" => "cancelled", "error" or "klippy_shutdown" or "klippy_disconnect" or "interrupted" or "server_exit" => "error", _ => null };
                                if (terminal is not null)
                                {
                                    status["print_stats"]!["state"] = terminal;
                                    observedAt = DateTimeOffset.UtcNow;
                                    dirty = true;
                                }
                            }
                            break;
                    }
                    if (dirty && tracker.Observe(status) is { } state) { Publish(state); dirty = false; }
                }
                var filename = status["print_stats"]?["filename"]?.ToString();
                if (filename != metadataFile)
                {
                    metadataFile = filename;
                    status.Remove("_openfarm_metadata");
                    dirty = true;
                    if (!string.IsNullOrWhiteSpace(filename))
                    {
                        try { status["_openfarm_metadata"] = await socket.CallAsync("server.files.metadata", new { filename }, _options.RequestTimeout, cancellationToken); }
                        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { LogOptionalFailed(_logger, "file metadata", ex); }
                    }
                }
                var now = DateTimeOffset.UtcNow;
                while (poller.Updates.TryRead(out var overrides)) { filament.Overrides = overrides; dirty = true; }
                if (now >= lateAt) { lateAt = DateTimeOffset.MaxValue; dirty = true; }
                if (dirty) Publish(null);
                // Moonraker pushes only changes, so an idle, cooled U1 is quiet for minutes. Any frame it sends
                // (camera wake answers every 2 s) shows it is there; without this the 90 s sweep marks it offline.
                if (socket.LastReceivedUtc - aliveAt >= _options.AliveInterval)
                    writer.TryWrite(new PrinterConnectorEvent(aliveAt = socket.LastReceivedUtc));
            }
        }
        finally
        {
            registration.Reset();
            await workers.CancelAsync();
            await cameraWorker;
            writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, ConnectionEnded: true));
        }
    }

    private async Task KeepCameraAwakeAsync(MoonrakerSocket socket, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await socket.CallAsync("camera.start_monitor", new { domain = "lan", interval = 0 }, _options.RequestTimeout, ct); }
                catch (Exception ex) when (!ct.IsCancellationRequested) { LogOptionalFailed(_logger, "camera wake", ex); }
                await Task.Delay(_options.CameraWakeInterval, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Optional U1 operation {Operation} failed.")]
    private static partial void LogOptionalFailed(ILogger logger, string operation, Exception exception);

    /// <summary>True once a notification is waiting or a telemetry interval passed; false when the socket closed.</summary>
    private async Task<bool> WaitForNotificationsAsync(MoonrakerSocket socket, CancellationToken cancellationToken)
    {
        using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        tickCts.CancelAfter(_options.TelemetryInterval);
        try
        {
            return await socket.Notifications.WaitToReadAsync(tickCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return true;
        }
    }

    private async Task<JsonObject> SubscribeAsync(MoonrakerSocket socket, CancellationToken cancellationToken)
    {
        var available = await socket.CallAsync("printer.objects.list", null, _options.RequestTimeout, cancellationToken);
        var names = (available?["objects"] as JsonArray ?? []).Select(n => n?.ToString()).ToHashSet();
        var objects = MoonrakerStatusMapper.Subscription.Where(p => names.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        var result = await socket.CallAsync("printer.objects.subscribe", new { objects },
            _options.RequestTimeout, cancellationToken);
        return result?["status"]?.DeepClone() as JsonObject
            ?? new JsonObject();
    }

    private async Task<JsonNode?> QueryExceptionsAsync(MoonrakerSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            var result = await socket.CallAsync("server.exception.query", null, _options.RequestTimeout, cancellationToken);
            return result?["exceptions"]?.DeepClone();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogOptionalFailed(_logger, "exception snapshot", ex);
            return null;
        }
    }

    /// <summary>Folds a <c>notify_status_update</c> delta into the merged status: changed fields replace, others stay.</summary>
    internal static void Merge(JsonObject status, JsonNode? delta)
    {
        if (delta is not JsonObject objects) return;
        foreach (var (name, fields) in objects)
        {
            if (fields is not JsonObject changed) continue;
            if (status[name] is not JsonObject current)
            {
                status[name] = changed.DeepClone();
                continue;
            }

            MergeFields(current, changed);
        }
    }


    private static void MergeFields(JsonObject current, JsonObject changed)
    {
        foreach (var (field, value) in changed)
        {
            if (value is JsonObject nested && current[field] is JsonObject retained) MergeFields(retained, nested);
            else current[field] = value?.DeepClone();
        }
    }

    /// <summary>
    /// Which state word each snapshot carries: the first one of a connection, then only changes. A print that ends by
    /// dropping straight back to standby still reports how it ended, because job tracking has no "idle" outcome.
    /// </summary>
    internal sealed class StateTracker
    {
        private bool _reported;


        public string? Current { get; private set; }

        public string? Observe(JsonObject status)
        {
            var state = MoonrakerStatusMapper.State(status);
            var wasPrinting = Current is "RUNNING" or "PAUSE";
            if (wasPrinting && MoonrakerStatusMapper.KlippyDown(status))
                state = "FAILED";
            else if (wasPrinting && state == "IDLE")
                // A direct standby transition is failed; history supplies authoritative completion.
                state = "FAILED";


            if (state is null || (_reported && state == Current)) return null;
            _reported = true;
            Current = state;
            return state;
        }
    }

}
