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

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>
/// Snapmaker U1 LAN connector over the printer's Moonraker (port 7125): plain HTTP for one-off requests and one
/// websocket per live connection for <c>printer.objects.subscribe</c> status pushes. The four toolheads are reported as
/// one four-tray unit whose tray id is the head index, so dispatch's tray mapping is the firmware's tool→head map.
/// Protocol notes and the hardware checklist: <c>dev-notes/SNAPMAKER_U1.md</c>.
/// </summary>
public sealed partial class SnapmakerU1Connector : IPrinterConnector
{
    private const string U1MachineType = "Snapmaker U1";

    // One pooled client for every printer (and their cameras); each call carries its own deadline.
    internal static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(30) })
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            using var infoRequest = Request(HttpMethod.Get, details, "/printer/info");
            using var infoResponse = await Http.SendAsync(infoRequest, timeout.Token);
            infoResponse.EnsureSuccessStatusCode();
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
    /// Sends firmware tool-routing macros, uploads missing G-code, then calls printer.print.start.
    /// A sliced project is unwrapped first: the U1 prints plain G-code, and its own 3MF handling expects Snapmaker's slicer.
    /// </summary>
    public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) =>
        StartJobAsync(details, fileContent, jobName, plate, feedMapping, null, cancellationToken);

    /// <inheritdoc cref="StartJobAsync(PrinterConnectionDetails, Stream, string, int, FilamentFeedMapping?, CancellationToken)"/>
    public async Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping,
        IProgress<PrintSendProgress>? progress, CancellationToken cancellationToken)
    {
        var gate = U1SessionRegistry.DispatchGate(details, _options);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var map = MapTable(feedMapping);
            var gcode = await PlateGCodeAsync(fileContent, plate, cancellationToken);
            var hash = Convert.ToHexStringLower(SHA256.HashData(gcode));
            var stem = System.Text.RegularExpressions.Regex.Replace(jobName, "[^a-zA-Z0-9_-]", "_");
            if (stem.Length > 100) stem = stem[..100];
            var fileName = $"openfarm-{stem}-{hash}.gcode";
            await using var lease = await U1SessionRegistry.BorrowAsync(details, _options, cancellationToken);
            var socket = lease.Socket;
            if (map.Length > 0)
            {
                var script = string.Join("\n", map.Select(p => $"SET_PRINT_EXTRUDER_MAP CONFIG_EXTRUDER={p[0]} MAP_EXTRUDER={p[1]}"))
                    + "\nSET_PRINT_USED_EXTRUDERS EXTRUDERS=" + string.Join(',', map.Select(p => p[1]));
                await socket.CallAsync("printer.gcode.script", new { script }, _options.RequestTimeout, cancellationToken);
            }
            var exists = false;
            try
            {
                await socket.CallAsync("server.files.metadata", new { filename = fileName }, _options.RequestTimeout, cancellationToken);
                exists = true;
            }
            catch (MoonrakerRpcException) { }
            if (!exists) await UploadAsync(details, gcode, fileName, progress, cancellationToken);
            progress?.Report(new PrintSendProgress(PrintSendStage.Starting));
            try
            {
                await socket.CallAsync("printer.print.start", new { filename = fileName }, _options.RequestTimeout, cancellationToken);
            }
            catch (Exception ex) when (ex is not MoonrakerRpcException && !cancellationToken.IsCancellationRequested)
            {
                throw new PrinterStartUnconfirmedException(ex);
            }
        }
        finally { gate.Release(); }
    }

    public async Task SkipObjectsAsync(PrinterConnectionDetails details, IReadOnlyList<string> objectIds, CancellationToken ct)
    {
        if (objectIds.Any(id => id.Length == 0 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '.' and not '-')))
            throw new InvalidOperationException("Invalid object name.");
        await using var lease = await U1SessionRegistry.BorrowAsync(details, _options, ct);
        var socket = lease.Socket;
        await socket.CallAsync("printer.gcode.script", new { script = string.Join("\n", objectIds.Select(id => "EXCLUDE_OBJECT NAME=" + id)) }, _options.RequestTimeout, ct);
    }

    public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PostAsync(details, "printer.print.pause", cancellationToken);

    public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PostAsync(details, "printer.print.resume", cancellationToken);

    public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PostAsync(details, "printer.print.cancel", cancellationToken);

    /// <summary>
    /// <c>[tool, head]</c> pairs for the firmware's tool→head map. The toolheads are one unit whose slot n is the spool on
    /// head n, so a tool's slot position is its head; a tool no line uses is left out.
    /// </summary>
    internal static int[][] MapTable(FilamentFeedMapping? feedMapping)
    {
        var pairs = (feedMapping?.Tools ?? []).Select((slot, tool) => (tool, slot)).Where(p => p.slot is not null).ToList();
        if (pairs.FirstOrDefault(p => p.slot is not { IsMultiMaterialUnit: true, UnitIndex: 0, SlotPosition: >= 0 and < MoonrakerStatusMapper.HeadCount })
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
        using var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        uploadCts.CancelAfter(_options.UploadTimeout);
        var file = new UploadContent(gcode, progress, () => uploadCts.CancelAfter(_options.UploadTimeout));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = Request(HttpMethod.Post, details, "/server/files/upload");
        request.Content = form;
        using var response = await Http.SendAsync(request, uploadCts.Token);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostAsync(PrinterConnectionDetails details, string path, CancellationToken cancellationToken)
    {
        await using var lease = await U1SessionRegistry.BorrowAsync(details, _options, cancellationToken);
        var socket = lease.Socket;
        await socket.CallWithStateAckAsync(path, _options.CommandTimeout, lease.Owned, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to Snapmaker {IpAddress} failed.")]
    private static partial void LogVerifyFailed(ILogger logger, string ipAddress, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer at {IpAddress} reports machine type '{MachineType}', not a Snapmaker U1.")]
    private static partial void LogUnexpectedMachine(ILogger logger, string ipAddress, string? machineType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer at {IpAddress} is not {SerialNumber} any more; not connecting. Edit the printer's IP address.")]
    private static partial void LogSerialMismatch(ILogger logger, string ipAddress, string serialNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moonraker connection to {IpAddress} ended; reconnecting.")]
    private static partial void LogSessionFailed(ILogger logger, string ipAddress, Exception exception);

    /// <summary>The file part of an upload, reporting the share of it written to the printer.</summary>
    internal sealed class UploadContent(byte[] bytes, IProgress<PrintSendProgress>? progress, Action? activity = null) : HttpContent
    {
        private const int ChunkSize = 64 * 1024;

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            for (var sent = 0; sent < bytes.Length;)
            {
                var count = Math.Min(ChunkSize, bytes.Length - sent);
                await stream.WriteAsync(bytes.AsMemory(sent, count), cancellationToken);
                sent += count;
                activity?.Invoke();
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
