using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>
/// Snapmaker U1 LAN connector over the printer's Moonraker (port 7125): plain HTTP for one-off requests and one
/// websocket per live connection for <c>printer.objects.subscribe</c> status pushes. The four toolheads are reported as
/// one four-tray unit whose tray id is the head index, so dispatch's tray mapping is the firmware's tool→head map.
/// Protocol notes and the hardware checklist: <c>docs/SNAPMAKER_U1.md</c>.
/// </summary>
public sealed partial class SnapmakerU1Connector : IPrinterConnector
{
    private const string U1MachineType = "Snapmaker U1";

    /// <summary>The folder under the gcodes root OpenFarm's uploads go to, apart from files people put there.</summary>
    internal const string UploadFolder = "openfarm";

    // One pooled client for every printer (and their cameras); each call carries its own deadline.
    internal static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly ILogger<SnapmakerU1Connector> _logger;
    private readonly MoonrakerTransportOptions _options;

    public SnapmakerU1Connector(ILogger<SnapmakerU1Connector> logger)
        : this(logger, new MoonrakerTransportOptions())
    {
    }

    internal SnapmakerU1Connector(ILogger<SnapmakerU1Connector> logger, MoonrakerTransportOptions options)
    {
        _logger = logger;
        _options = options;
    }

    /// <summary>Answers as a U1, and — once registered — as the same U1 (its serial), so an IP edit can't adopt another printer.</summary>
    public async Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        try
        {
            var product = await ReadProductInfoAsync(details, cancellationToken);
            if (!string.Equals(product.MachineType, U1MachineType, StringComparison.OrdinalIgnoreCase))
            {
                LogUnexpectedMachine(_logger, details.IpAddress, product.MachineType);
                return false;
            }

            return string.IsNullOrWhiteSpace(details.SerialNumber) || IsSerial(product.SerialNumber, details.SerialNumber);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            LogVerifyFailed(_logger, details.IpAddress, ex);
            return false;
        }
    }

    public async Task<string?> ReadSerialNumberAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        try
        {
            var serialNumber = (await ReadProductInfoAsync(details, cancellationToken)).SerialNumber;
            return string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            LogVerifyFailed(_logger, details.IpAddress, ex);
            return null;
        }
    }

    public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateUnbounded<PrinterConnectorEvent>();
        var sessions = RunSessionsAsync(details, channel.Writer, connectionCts.Token);
        try
        {
            await foreach (var connectorEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return connectorEvent;
            }
        }
        finally
        {
            await connectionCts.CancelAsync();
            await sessions;
        }
    }

    /// <summary>
    /// Uploads the plate's G-code to the gcodes root, then starts it with <c>server.files.start_local_print</c> (firmware
    /// 1.3+, websocket only), whose <c>map_table</c> routes each of the file's tools to the head its spool is loaded in.
    /// A sliced project is unwrapped first: the U1 prints plain G-code, and its own 3MF handling expects Snapmaker's slicer.
    /// </summary>
    public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) =>
        StartJobAsync(details, fileContent, jobName, plate, feedMapping, null, cancellationToken);

    /// <inheritdoc cref="StartJobAsync(PrinterConnectionDetails, Stream, string, int, FilamentFeedMapping?, CancellationToken)"/>
    public async Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping,
        IProgress<PrintSendProgress>? progress, CancellationToken cancellationToken)
    {
        var gcode = await PlateGCodeAsync(fileContent, plate, cancellationToken);
        var fileName = PrintFileName.ForUpload(jobName, ".gcode");
        await UploadAsync(details, gcode, fileName, progress, cancellationToken);
        progress?.Report(new PrintSendProgress(PrintSendStage.Starting));

        var path = $"{UploadFolder}/{fileName}";
        // No mapping keeps the firmware's identity map (tool n on head n).
        object parameters = MapTable(feedMapping) is { Length: > 0 } mapTable
            ? new { path, options = new { map_table = mapTable } }
            : new { path };
        await using var socket = await MoonrakerSocket.ConnectAsync(BaseUri(details), details.AccessCode, _options, cancellationToken);
        var result = await socket.CallAsync("server.files.start_local_print", parameters, _options.StartTimeout, cancellationToken);
        if (result?["state"]?.ToString() != "success")
            throw new IOException($"The printer did not start the print: {result?["message"]?.ToString() ?? "no answer"}");
    }

    public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PostAsync(details, "/printer/print/pause", cancellationToken);

    public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PostAsync(details, "/printer/print/resume", cancellationToken);

    public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PostAsync(details, "/printer/print/cancel", cancellationToken);

    /// <summary>
    /// <c>[tool, head]</c> pairs for the firmware's tool→head map. The toolheads are one unit whose slot n is the spool on
    /// head n, so a tool's slot position is its head; a tool no line uses is left out.
    /// </summary>
    internal static int[][] MapTable(FilamentFeedMapping? feedMapping)
    {
        var pairs = (feedMapping?.Tools ?? []).Select((slot, tool) => (tool, slot)).Where(p => p.slot is not null).ToList();
        if (pairs.FirstOrDefault(p => p.slot is not { IsMultiMaterialUnit: true, UnitIndex: 0, SlotPosition: < MoonrakerStatusMapper.HeadCount })
            is { slot: { } unknown })
            throw new InvalidOperationException($"A Snapmaker U1 feeds from its {MoonrakerStatusMapper.HeadCount} heads; the feed mapping names {unknown}.");
        return pairs.Select(p => new[] { p.tool, p.slot!.Value.SlotPosition }).ToArray();
    }

    // The plate's G-code inside an OrcaSlicer / Bambu Studio project; bare G-code is sent as is.
    private static async Task<byte[]> PlateGCodeAsync(Stream fileContent, int plate, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await fileContent.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (bytes is not [0x50, 0x4B, 0x03, 0x04, ..]) return bytes;

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var path = $"Metadata/plate_{plate}.gcode";
        var entry = archive.GetEntry(path) ?? throw new IOException($"The print project has no {path}.");
        using var gcode = new MemoryStream();
        await using (var content = entry.Open())
            await content.CopyToAsync(gcode, cancellationToken);
        return gcode.ToArray();
    }

    private async Task UploadAsync(PrinterConnectionDetails details, byte[] gcode, string fileName, IProgress<PrintSendProgress>? progress, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        // Arguments before the file, so they are read before the upload completes.
        form.Add(new StringContent("gcodes"), "root");
        form.Add(new StringContent(UploadFolder), "path");
        // Moonraker compares this against its own digest and rejects a corrupted transfer.
        form.Add(new StringContent(Convert.ToHexStringLower(SHA256.HashData(gcode))), "checksum");
        var file = new UploadContent(gcode, progress);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        uploadCts.CancelAfter(_options.UploadTimeout);
        using var request = Request(HttpMethod.Post, details, "/server/files/upload");
        request.Content = form;
        using var response = await Http.SendAsync(request, uploadCts.Token);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostAsync(PrinterConnectionDetails details, string path, CancellationToken cancellationToken)
    {
        using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        commandCts.CancelAfter(_options.CommandTimeout);
        using var request = Request(HttpMethod.Post, details, path);
        using var response = await Http.SendAsync(request, commandCts.Token);
        response.EnsureSuccessStatusCode();
    }

    /// <summary><paramref name="gcodeFile"/> is <c>print_stats.filename</c>: a path under the printer's gcodes root.</summary>
    public async Task<byte[]?> FetchPrintFileAsync(PrinterConnectionDetails details, string? jobName, string? gcodeFile, CancellationToken cancellationToken)
    {
        var segments = gcodeFile?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length == 0 || segments.Any(s => s is "." or "..")) return null;

        using var request = Request(HttpMethod.Get, details, $"/server/files/gcodes/{string.Join('/', segments.Select(Uri.EscapeDataString))}");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length > 0 ? bytes : null;
    }

    /// <summary>
    /// Reconnects for as long as the connection is wanted. The state tracker outlives each websocket, so a Wi-Fi blip
    /// never re-sends a state the printer already reported — re-sending FINISH would undo an operator's Mark ready.
    /// </summary>
    private async Task RunSessionsAsync(PrinterConnectionDetails details, ChannelWriter<PrinterConnectorEvent> writer, CancellationToken cancellationToken)
    {
        var tracker = new StateTracker();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var socket = await MoonrakerSocket.ConnectAsync(BaseUri(details), details.AccessCode, _options, cancellationToken);
                    // Moonraker messages carry no serial, so a DHCP swap would book another printer's telemetry here.
                    if (await IsSamePrinterAsync(details, cancellationToken))
                        await RunSessionAsync(socket, tracker, writer, cancellationToken);
                    else
                        LogSerialMismatch(_logger, details.IpAddress, details.SerialNumber);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    LogSessionFailed(_logger, details.IpAddress, ex);
                }

                await Task.Delay(_options.ReconnectDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task RunSessionAsync(MoonrakerSocket socket, StateTracker tracker, ChannelWriter<PrinterConnectorEvent> writer, CancellationToken cancellationToken)
    {
        var status = await SubscribeAsync(socket, cancellationToken);
        var exceptions = await QueryExceptionsAsync(socket, cancellationToken);
        writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, ConnectionStarted: true));
        try
        {
            var lastEmitUtc = Emit(writer, status, exceptions, tracker.Observe(status), tracker.Current);
            var dirty = false;
            while (true)
            {
                if (!await WaitForNotificationsAsync(socket, cancellationToken)) return;

                while (socket.Notifications.TryRead(out var notification))
                {
                    var method = notification["method"]?.ToString();
                    var payload = notification["params"] is JsonArray { Count: > 0 } parameters ? parameters[0] : null;
                    var alertsChanged = false;
                    switch (method)
                    {
                        case "notify_status_update":
                            Merge(status, payload);
                            break;
                        case "notify_klippy_ready":
                            status = await SubscribeAsync(socket, cancellationToken);
                            break;
                        case "notify_klippy_shutdown" or "notify_klippy_disconnected":
                            // Klippy no longer answers; a print in progress is lost (StateTracker reports it as failed).
                            status["webhooks"] = new JsonObject { ["state"] = "shutdown" };
                            break;
                        case { } name when name.EndsWith("exception_status", StringComparison.Ordinal):
                            exceptions = payload?["exceptions"]?.DeepClone();
                            alertsChanged = true;
                            break;
                        default:
                            continue;
                    }

                    // A state change goes out at once and in order, never folded into the next telemetry tick.
                    dirty = true;
                    if (tracker.Observe(status) is { } state)
                        lastEmitUtc = Emit(writer, status, exceptions, state, state);
                    else if (alertsChanged)
                        lastEmitUtc = Emit(writer, status, exceptions, null, tracker.Current);
                }

                var sinceEmit = DateTimeOffset.UtcNow - lastEmitUtc;
                if ((dirty && sinceEmit >= _options.TelemetryInterval) || sinceEmit >= _options.TelemetryFloor)
                {
                    lastEmitUtc = Emit(writer, status, exceptions, null, tracker.Current);
                    dirty = false;
                }
            }
        }
        finally
        {
            writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, ConnectionEnded: true));
        }
    }

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

    private static DateTimeOffset Emit(ChannelWriter<PrinterConnectorEvent> writer, JsonObject status, JsonNode? exceptions, string? state, string? currentState)
    {
        var now = DateTimeOffset.UtcNow;
        writer.TryWrite(new PrinterConnectorEvent(now,
            GcodeState: state,
            Telemetry: MoonrakerStatusMapper.Telemetry(status, currentState),
            HmsAlerts: MoonrakerStatusMapper.Alerts(exceptions)));
        return now;
    }

    private async Task<JsonObject> SubscribeAsync(MoonrakerSocket socket, CancellationToken cancellationToken)
    {
        var result = await socket.CallAsync("printer.objects.subscribe", new { objects = MoonrakerStatusMapper.Subscription },
            _options.RequestTimeout, cancellationToken);
        return result?["status"]?.DeepClone() as JsonObject
            ?? throw new InvalidOperationException("Moonraker answered printer.objects.subscribe without a status.");
    }

    private async Task<JsonNode?> QueryExceptionsAsync(MoonrakerSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            var result = await socket.CallAsync("server.exception.query", null, _options.RequestTimeout, cancellationToken);
            return result?["exceptions"]?.DeepClone();
        }
        catch (MoonrakerRpcException)
        {
            // Firmware without Snapmaker's exception manager: no alerts, the rest still works.
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

            foreach (var (field, value) in changed)
                current[field] = value?.DeepClone();
        }
    }

    private async Task<bool> IsSamePrinterAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(details.SerialNumber)
        || IsSerial((await ReadProductInfoAsync(details, cancellationToken)).SerialNumber, details.SerialNumber);

    private async Task<(string? MachineType, string? SerialNumber)> ReadProductInfoAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(_options.RequestTimeout);
        using var request = Request(HttpMethod.Get, details, "/machine/system_info");
        using var response = await Http.SendAsync(request, requestCts.Token);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(requestCts.Token));
        var product = body?["result"]?["system_info"]?["product_info"];
        return (product?["machine_type"]?.ToString(), product?["serial_number"]?.ToString());
    }

    private HttpRequestMessage Request(HttpMethod method, PrinterConnectionDetails details, string path) =>
        Request(method, details, _options.Port, path);

    internal static HttpRequestMessage Request(HttpMethod method, PrinterConnectionDetails details, int port, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri(details, port), path));
        if (!string.IsNullOrWhiteSpace(details.AccessCode)) request.Headers.Add("X-Api-Key", details.AccessCode);
        return request;
    }

    private Uri BaseUri(PrinterConnectionDetails details) => BaseUri(details, _options.Port);

    internal static Uri BaseUri(PrinterConnectionDetails details, int port) => new UriBuilder(Uri.UriSchemeHttp, details.IpAddress, port).Uri;

    private static bool IsSerial(string? reported, string expected) => string.Equals(reported, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException;

    /// <summary>
    /// Which state word each snapshot carries: the first one of a connection, then only changes. A print that ends by
    /// dropping straight back to standby still reports how it ended, because job tracking has no "idle" outcome.
    /// </summary>
    internal sealed class StateTracker
    {
        private bool _reported;
        private int? _lastProgress;

        public string? Current { get; private set; }

        public string? Observe(JsonObject status)
        {
            var state = MoonrakerStatusMapper.State(status);
            var wasPrinting = Current is "RUNNING" or "PAUSE";
            if (wasPrinting && MoonrakerStatusMapper.KlippyDown(status))
                state = "FAILED";
            else if (wasPrinting && state == "IDLE")
                // ponytail: judged by the last progress seen; read server.history.list for the real outcome if this misjudges.
                state = _lastProgress >= PrintEnd.FinishedPercent ? "FINISH" : "FAILED";

            if (state is "RUNNING" or "PAUSE")
                _lastProgress = MoonrakerStatusMapper.ProgressPercent(status) ?? _lastProgress;

            if (state is null || (_reported && state == Current)) return null;
            _reported = true;
            Current = state;
            return state;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to Snapmaker {IpAddress} failed.")]
    private static partial void LogVerifyFailed(ILogger logger, string ipAddress, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer at {IpAddress} reports machine type '{MachineType}', not a Snapmaker U1.")]
    private static partial void LogUnexpectedMachine(ILogger logger, string ipAddress, string? machineType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer at {IpAddress} is not {SerialNumber} any more; not connecting. Edit the printer's IP address.")]
    private static partial void LogSerialMismatch(ILogger logger, string ipAddress, string serialNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moonraker connection to {IpAddress} ended; reconnecting.")]
    private static partial void LogSessionFailed(ILogger logger, string ipAddress, Exception exception);

    /// <summary>The file part of an upload, reporting the share of it written to the printer.</summary>
    internal sealed class UploadContent(byte[] bytes, IProgress<PrintSendProgress>? progress) : HttpContent
    {
        private const int ChunkSize = 64 * 1024;

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            for (var sent = 0; sent < bytes.Length;)
            {
                var count = Math.Min(ChunkSize, bytes.Length - sent);
                await stream.WriteAsync(bytes.AsMemory(sent, count), cancellationToken);
                sent += count;
                progress?.Report(new PrintSendProgress(PrintSendStage.Uploading, (int)(100L * sent / bytes.Length)));
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long length)
        {
            length = bytes.Length;
            return true;
        }
    }
}
