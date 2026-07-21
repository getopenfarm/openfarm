using System.Security.Cryptography;
using FluentAssertions;
using OpenFarm.Tools.ProvisionSlicers;

namespace OpenFarm.Tools.ProvisionSlicers.Tests;

public class IntegrityVerificationTests : IDisposable
{
    private readonly string _outputRoot = Directory.CreateTempSubdirectory("openfarm-provision-integrity-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outputRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ProvisionAsync_FirstFetchOfNewEntry_RecordsTheComputedDigest()
    {
        var assetBytes = new byte[] { 1, 2, 3, 4 };
        var expectedDigest = Convert.ToHexStringLower(SHA256.HashData(assetBytes));
        var provisioner = new Provisioner(new FakeAssetDownloader(assetBytes), new FakeAssetExtractor());
        var lockfile = Lockfile.CreateEmpty();
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.2.0", Platform.Linux);

        var result = await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None);

        result.Outcome.Should().Be(ProvisionOutcome.Provisioned);
        lockfile.Find(Engine.OrcaSlicer, "2.2.0", Platform.Linux)!.Sha256.Should().Be(expectedDigest);
    }

    [Fact]
    public async Task ProvisionAsync_DownloadedBytesDoNotMatchLockfileDigest_ReportsFailedWithoutThrowing()
    {
        var lockfile = Lockfile.CreateEmpty();
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.2.0", Platform.Linux);
        lockfile.Set(new LockfileEntry(Engine.OrcaSlicer, "2.2.0", Platform.Linux, "https://example.test/asset", "0000000000000000000000000000000000000000000000000000000000000000"));

        // The leaf directory does not exist on disk yet, so the "already present" short-circuit
        // doesn't apply and the digest is actually re-checked against the (mismatching) download.
        var provisioner = new Provisioner(new FakeAssetDownloader([9, 9, 9]), new FakeAssetExtractor());

        // No try/catch here is deliberate — a mismatch must surface as a reported result, not an
        // exception; an uncaught throw would fail this test on its own.
        var result = await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None);

        result.Outcome.Should().Be(ProvisionOutcome.Failed);
        result.Message.Should().Contain("mismatch");
    }

    private sealed class FakeAssetDownloader(byte[] bytes) : IAssetDownloader
    {
        public Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken) => Task.FromResult(bytes);
    }

    private sealed class FakeAssetExtractor : IAssetExtractor
    {
        public Task ExtractAsync(byte[] assetBytes, string assetFileName, string destinationLeafDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destinationLeafDirectory);
            File.WriteAllBytes(Path.Combine(destinationLeafDirectory, "AppRun"), assetBytes);
            return Task.CompletedTask;
        }
    }
}
