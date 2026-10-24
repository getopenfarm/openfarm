using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>
/// Snapmaker U1 camera on stock firmware: the printer keeps a JPEG at <c>/server/files/camera/monitor.jpg</c> fresh only
/// while a client keeps it awake with <c>camera.start_monitor</c> (it idles a few seconds after the last one). This
/// wakes it, polls the snapshot, and yields a frame only when it changed. Any failure ends the stream; the connection
/// service's camera loop starts a new one.
/// </summary>
public sealed partial class SnapmakerU1CameraRelay : IPrinterCameraRelay
{
    private const string SnapshotPath = "/server/files/camera/monitor.jpg";

    private readonly ILogger<SnapmakerU1CameraRelay> _logger;
    private readonly MoonrakerTransportOptions _options;

    public SnapmakerU1CameraRelay(ILogger<SnapmakerU1CameraRelay> logger)
        : this(logger, new MoonrakerTransportOptions())
    {
    }

    internal SnapmakerU1CameraRelay(ILogger<SnapmakerU1CameraRelay> logger, MoonrakerTransportOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var socket = await MoonrakerSocket.ConnectAsync(
            SnapmakerU1Connector.BaseUri(details, _options.Port), details.AccessCode, _options, cancellationToken);
        var wokeAtUtc = DateTimeOffset.MinValue;
        byte[]? last = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow - wokeAtUtc >= _options.CameraWakeInterval)
            {
                await socket.CallAsync("camera.start_monitor", new { domain = "lan", interval = 0 }, _options.RequestTimeout, cancellationToken);
                wokeAtUtc = DateTimeOffset.UtcNow;
            }

            var frame = await FetchSnapshotAsync(details, cancellationToken);
            if (frame is not null && (last is null || !frame.AsSpan().SequenceEqual(last)))
            {
                last = frame;
                yield return frame;
            }

            await Task.Delay(_options.CameraPollInterval, cancellationToken);
        }
    }

    /// <summary>Null while there is no snapshot yet (the camera is waking) or it isn't a whole JPEG (caught mid-write).</summary>
    private async Task<byte[]?> FetchSnapshotAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(_options.RequestTimeout);
        using var request = SnapmakerU1Connector.Request(HttpMethod.Get, details, _options.Port, SnapshotPath);
        using var response = await SnapmakerU1Connector.Http.SendAsync(request, requestCts.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var frame = await response.Content.ReadAsByteArrayAsync(requestCts.Token);
        if (JpegFrame.IsValid(frame)) return frame;

        LogPartialFrame(_logger, details.IpAddress, frame.Length);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped a camera snapshot from {IpAddress} that is not a whole JPEG ({Length} bytes).")]
    private static partial void LogPartialFrame(ILogger logger, string ipAddress, int length);
}
