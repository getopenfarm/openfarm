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

/// <summary>Real implementation — downloads a GitHub release asset over HTTPS. Manual-only, never a CI gate (FR-018 keeps this out of the running app entirely).</summary>
public sealed class HttpAssetDownloader : IAssetDownloader, IDisposable
{
    private readonly HttpClient _httpClient = new();

    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();
}
