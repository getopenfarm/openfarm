namespace OpenFarm.Tools.ProvisionSlicers;

/// <summary>
/// Fetches one release asset's raw bytes. The real implementation (<see cref="HttpAssetDownloader"/>)
/// is the only place in the whole solution that ever calls out to GitHub — kept behind this seam so
/// the idempotency/integrity pipeline (<see cref="Provisioner"/>) can be unit-tested against a fake,
/// never a live network call (research.md §8; real download validated manually, same carve-out as
/// the real Bambu LAN connector).
/// </summary>
public interface IAssetDownloader
{
    Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// Real implementation — downloads a GitHub release asset over HTTPS. Manual-only, never a CI gate (FR-018 keeps this out of the running app entirely).
/// <see cref="HttpClient.Timeout"/> only covers the response headers here, so a CDN that stops sending mid-body
/// would hang forever: every read gets a stall timeout, and a stalled/dropped download resumes with a Range request.
/// </summary>
public sealed class HttpAssetDownloader : IAssetDownloader, IDisposable
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 5;

    private readonly HttpClient _httpClient = new();

    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ReadIntoAsync(url, buffer, cancellationToken);
                Console.Error.WriteLine();
                return buffer.ToArray();
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested
                                       && ex is HttpRequestException or IOException or OperationCanceledException)
            {
                Console.Error.WriteLine($"\n  stalled/failed at {buffer.Length / 1_048_576} MB ({ex.Message}); resuming (attempt {attempt + 1}/{MaxAttempts})…");
            }
        }
    }

    private async Task ReadIntoAsync(string url, MemoryStream buffer, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(StallTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (buffer.Length > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(buffer.Length, null);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();

        // 200 to a Range request = server ignored it; start over rather than append a second copy.
        if (buffer.Length > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            buffer.SetLength(0);

        var total = response.Content.Headers.ContentLength is { } len ? len + buffer.Length : (long?)null;
        await using var body = await response.Content.ReadAsStreamAsync(stall.Token);
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, stall.Token)) > 0)
        {
            stall.CancelAfter(StallTimeout);
            buffer.Write(chunk, 0, read);
            Console.Error.Write($"\r  {buffer.Length / 1_048_576} / {(total is { } t ? (t / 1_048_576).ToString() : "?")} MB   ");
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
