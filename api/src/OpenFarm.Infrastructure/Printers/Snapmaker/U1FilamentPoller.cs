using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>Session-scoped conditional polling, independent of status processing. Transfers owned snapshots to the reader.</summary>
internal sealed partial class U1FilamentPoller : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly SemaphoreSlim _refresh = new(0, 1);
    private readonly Channel<JsonNode?> _updates = Channel.CreateUnbounded<JsonNode?>();
    private readonly Task _worker;
    internal ChannelReader<JsonNode?> Updates => _updates.Reader;

    internal U1FilamentPoller(PrinterConnectionDetails details, MoonrakerTransportOptions options, ILogger logger, CancellationToken ct)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _worker = RunAsync(details, options, logger, _stop.Token);
    }

    internal void Refresh() { if (_refresh.CurrentCount == 0) _refresh.Release(); }

    private async Task RunAsync(PrinterConnectionDetails details, MoonrakerTransportOptions options, ILogger logger, CancellationToken ct)
    {
        string? modified = null;
        string? previous = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(options.RequestTimeout);
                    using var request = SnapmakerU1Connector.Request(HttpMethod.Get, details, options.Port, "/server/files/config/snapmaker/print_task.json");
                    if (modified is not null) request.Headers.TryAddWithoutValidation("If-Modified-Since", modified);
                    using var response = await SnapmakerU1Connector.Http.SendAsync(request, timeout.Token);
                    if (response.StatusCode != HttpStatusCode.NotModified)
                    {
                        JsonNode? next = null;
                        if (response.StatusCode == HttpStatusCode.NotFound) modified = null;
                        else
                        {
                            response.EnsureSuccessStatusCode();
                            next = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token)) as JsonObject
                                ?? throw new System.Text.Json.JsonException("Expected a U1 print task object.");
                            modified = response.Content.Headers.LastModified?.ToString("R");
                        }
                        var serialized = next?.ToJsonString();
                        if (serialized != previous) { previous = serialized; _updates.Writer.TryWrite(next); }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested) { LogPollFailed(logger, ex); }
                await _refresh.WaitAsync(TimeSpan.FromSeconds(3), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { _updates.Writer.TryComplete(); }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "U1 filament override poll failed; retaining previous readings.")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _worker;
        _refresh.Dispose();
        _stop.Dispose();
    }
}
