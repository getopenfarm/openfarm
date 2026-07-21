using System.Security.Cryptography;

namespace OpenFarm.Tools.ProvisionSlicers;

public sealed record ProvisionEntryRequest(Engine Engine, string Version, Platform Platform);

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
/// throws for an expected per-entry failure (unresolvable asset, download error, digest mismatch,
/// extraction error) — always returns a <see cref="ProvisionEntryResult"/> so one bad entry can't
/// abort the run (FR-015/FR-017).
/// </summary>
public sealed class Provisioner(IAssetDownloader downloader, IAssetExtractor extractor)
{
    public async Task<ProvisionEntryResult> ProvisionAsync(
        ProvisionEntryRequest request, string outputRoot, Lockfile lockfile, CancellationToken cancellationToken)
    {
        var leafDirectory = Path.Combine(outputRoot, EngineSlug(request.Engine), request.Version, PlatformSlug(request.Platform));
        var existingLockEntry = lockfile.Find(request.Engine, request.Version, request.Platform);

        if (existingLockEntry is not null && Directory.Exists(leafDirectory) && Directory.EnumerateFileSystemEntries(leafDirectory).Any())
        {
            return Result(request, ProvisionOutcome.AlreadyPresent, "Already present.");
        }

        var releaseSourceResult = EngineReleaseSources.Resolve(request.Engine, request.Version, request.Platform);
        if (!releaseSourceResult.Success)
        {
            return Result(request, ProvisionOutcome.Failed, releaseSourceResult.Error!);
        }

        var source = releaseSourceResult.Source!;
        var assetUrl = $"https://github.com/{source.Repo}/releases/download/v{request.Version}/{source.AssetName}";

        byte[] assetBytes;
        try
        {
            assetBytes = await downloader.DownloadAsync(assetUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(request, ProvisionOutcome.Failed, $"Download failed: {ex.Message}");
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(assetBytes));

        if (existingLockEntry is not null && !string.Equals(existingLockEntry.Sha256, digest, StringComparison.OrdinalIgnoreCase))
        {
            return Result(request, ProvisionOutcome.Failed,
                $"SHA-256 mismatch against slicers.lock.json (expected {existingLockEntry.Sha256}, computed {digest}).");
        }

        try
        {
            await extractor.ExtractAsync(assetBytes, source.AssetName, leafDirectory, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(request, ProvisionOutcome.Failed, $"Extraction failed: {ex.Message}");
        }

        // First fetch of a new entry records the computed digest (research.md §8) — subsequent runs
        // verify against it.
        lockfile.Set(new LockfileEntry(request.Engine, request.Version, request.Platform, assetUrl, digest));

        return Result(request, ProvisionOutcome.Provisioned, "Provisioned.");
    }

    public static string EngineSlug(Engine engine) => engine.ToString().ToLowerInvariant();

    public static string PlatformSlug(Platform platform) => platform.ToString().ToLowerInvariant();

    private static ProvisionEntryResult Result(ProvisionEntryRequest request, ProvisionOutcome outcome, string message) =>
        new(request.Engine, request.Version, request.Platform, outcome, message);
}
