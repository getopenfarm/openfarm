using System.Security.Cryptography;

namespace OpenFarm.Tools.ProvisionSlicers;

/// <summary><paramref name="AssetUrl"/> comes straight from the manifest (contracts/slicer-manifest.schema.json)
/// — the tool never derives or guesses an asset URL/filename itself, since each engine's release asset
/// naming convention can and does change between versions.</summary>
public sealed record ProvisionEntryRequest(Engine Engine, string Version, Platform Platform, string AssetUrl, string? ProfilesUrl = null);

public enum ProvisionOutcome
{
    AlreadyPresent,
    Provisioned,
    Failed,
}

public sealed record ProvisionEntryResult(Engine Engine, string Version, Platform Platform, ProvisionOutcome Outcome, string Message);

/// <summary>
/// The download → SHA-256 verify → extract pipeline for one manifest entry, placing it at
/// <c>&lt;out&gt;/&lt;engineSlug&gt;/&lt;version&gt;/&lt;platformSlug&gt;/…</c> — matching
/// <c>BundledSlicerScanner</c>'s convention exactly (research.md §1/§9). Idempotent: a leaf already
/// present with a lockfile digest match is skipped and reported "already present" (FR-015). Never
/// throws for an expected per-entry failure (download error, digest mismatch, extraction error) —
/// always returns a <see cref="ProvisionEntryResult"/> so one bad entry can't abort the run
/// (FR-015/FR-017). <paramref name="onProgress"/> is optional, stage-by-stage progress reporting
/// (Program.cs wires it to Console.WriteLine; tests omit it) — provisioning a large binary with no
/// output in between made a hung/slow run indistinguishable from a stuck one.
/// </summary>
public sealed class Provisioner(IAssetDownloader downloader, IAssetExtractor extractor)
{
    public async Task<ProvisionEntryResult> ProvisionAsync(
        ProvisionEntryRequest request, string outputRoot, Lockfile lockfile, CancellationToken cancellationToken, Action<string>? onProgress = null)
    {
        void Log(string message) => onProgress?.Invoke($"[{request.Engine} {request.Version} ({request.Platform})] {message}");

        var leafDirectory = Path.Combine(outputRoot, EngineSlug(request.Engine), request.Version, PlatformSlug(request.Platform));
        var existingLockEntry = lockfile.Find(request.Engine, request.Version, request.Platform);

        if (existingLockEntry is not null && Directory.Exists(leafDirectory) && Directory.EnumerateFileSystemEntries(leafDirectory).Any())
        {
            Log("Already present — skipping.");
            return Result(request, ProvisionOutcome.AlreadyPresent, "Already present.");
        }

        var (digest, failure) = await FetchAndExtractAsync(request.AssetUrl, existingLockEntry?.Sha256, leafDirectory, Log, cancellationToken);
        if (failure is not null)
        {
            return Result(request, ProvisionOutcome.Failed, failure);
        }

        string? profilesDigest = null;
        if (request.ProfilesUrl is { } profilesUrl)
        {
            (profilesDigest, failure) = await FetchAndExtractAsync(
                profilesUrl, existingLockEntry?.ProfilesSha256, Path.Combine(leafDirectory, "profiles"), Log, cancellationToken);
            if (failure is not null)
            {
                return Result(request, ProvisionOutcome.Failed, $"Profiles: {failure}");
            }
        }

        // First fetch of a new entry records the computed digests (research.md §8) — subsequent runs
        // verify against them. Recorded only once everything is in place, so a half-provisioned leaf is retried.
        lockfile.Set(new LockfileEntry(request.Engine, request.Version, request.Platform, request.AssetUrl, digest!, request.ProfilesUrl, profilesDigest));

        Log("Provisioned.");
        return Result(request, ProvisionOutcome.Provisioned, "Provisioned.");
    }

    /// <summary>Download → SHA-256 verify against <paramref name="expectedSha256"/> (when locked) → extract. Returns the digest, or why it failed.</summary>
    private async Task<(string? Digest, string? Failure)> FetchAndExtractAsync(
        string url, string? expectedSha256, string destination, Action<string> log, CancellationToken cancellationToken)
    {
        log($"Downloading {url} …");

        byte[] bytes;
        try
        {
            bytes = await downloader.DownloadAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log($"Download failed: {ex.Message}");
            return (null, $"Download failed: {ex.Message}");
        }

        log($"Downloaded {bytes.Length:N0} bytes. Verifying SHA-256…");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (expectedSha256 is not null && !string.Equals(expectedSha256, digest, StringComparison.OrdinalIgnoreCase))
        {
            log($"SHA-256 mismatch (expected {expectedSha256}, computed {digest}).");
            return (null, $"SHA-256 mismatch against slicers.lock.json (expected {expectedSha256}, computed {digest}).");
        }

        var fileName = GetAssetFileName(url);
        log($"Extracting {fileName}…");

        try
        {
            await extractor.ExtractAsync(bytes, fileName, destination, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log($"Extraction failed: {ex.Message}");
            return (null, $"Extraction failed: {ex.Message}");
        }

        return (digest, null);
    }

    public static string EngineSlug(Engine engine) => engine.ToString().ToLowerInvariant();

    public static string PlatformSlug(Platform platform) => platform.ToString().ToLowerInvariant();

    private static string GetAssetFileName(string assetUrl) =>
        Uri.TryCreate(assetUrl, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : Path.GetFileName(assetUrl);

    private static ProvisionEntryResult Result(ProvisionEntryRequest request, ProvisionOutcome outcome, string message) =>
        new(request.Engine, request.Version, request.Platform, outcome, message);
}
