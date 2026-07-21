using FluentAssertions;
using OpenFarm.Tools.ProvisionSlicers;

namespace OpenFarm.Tools.ProvisionSlicers.Tests;

public class ProvisioningIdempotencyTests : IDisposable
{
    private readonly string _outputRoot = Directory.CreateTempSubdirectory("openfarm-provision-idempotency-").FullName;

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
    public async Task ProvisionAsync_EntryNotYetPresent_DownloadsAndReportsProvisioned()
    {
        var downloader = new FakeAssetDownloader([1, 2, 3]);
        var provisioner = new Provisioner(downloader, new FakeAssetExtractor());
        var lockfile = Lockfile.CreateEmpty();
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.2.0", Platform.Linux);

        var result = await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None);

        result.Outcome.Should().Be(ProvisionOutcome.Provisioned);
        downloader.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ProvisionAsync_ReRunAfterProvisioning_SkipsAsAlreadyPresent()
    {
        var downloader = new FakeAssetDownloader([1, 2, 3]);
        var provisioner = new Provisioner(downloader, new FakeAssetExtractor());
        var lockfile = Lockfile.CreateEmpty();
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.2.0", Platform.Linux);

        await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None);
        var secondResult = await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None);

        secondResult.Outcome.Should().Be(ProvisionOutcome.AlreadyPresent);
        downloader.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ProvisionAsync_RunTwiceEndToEnd_ProducesIdenticalLockfileEntries()
    {
        var lockfilePath = Path.Combine(_outputRoot, "slicers.lock.json");
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.2.0", Platform.Linux);

        var lockfile1 = Lockfile.CreateEmpty();
        var provisioner1 = new Provisioner(new FakeAssetDownloader([1, 2, 3]), new FakeAssetExtractor());
        await provisioner1.ProvisionAsync(request, _outputRoot, lockfile1, CancellationToken.None);
        lockfile1.Save(lockfilePath);

        var lockfile2 = Lockfile.Load(lockfilePath);
        var provisioner2 = new Provisioner(new FakeAssetDownloader([1, 2, 3]), new FakeAssetExtractor());
        var result2 = await provisioner2.ProvisionAsync(request, _outputRoot, lockfile2, CancellationToken.None);
        lockfile2.Save(lockfilePath);

        result2.Outcome.Should().Be(ProvisionOutcome.AlreadyPresent);
        var firstContent = File.ReadAllText(lockfilePath);
        lockfile2.Save(lockfilePath);
        File.ReadAllText(lockfilePath).Should().Be(firstContent);
    }

    private sealed class FakeAssetDownloader(byte[] bytes) : IAssetDownloader
    {
        public int CallCount { get; private set; }

        public Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(bytes);
        }
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
