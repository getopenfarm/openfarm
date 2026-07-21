using FluentAssertions;
using OpenFarm.Tools.ProvisionSlicers;

namespace OpenFarm.Tools.ProvisionSlicers.Tests;

/// <summary>
/// A large binary download/extract with no output in between is indistinguishable from a hung run —
/// <see cref="Provisioner.ProvisionAsync"/>'s optional progress callback exists so an operator watching
/// the console can tell what stage a provisioning run is at.
/// </summary>
public class ProvisioningProgressTests : IDisposable
{
    private readonly string _outputRoot = Directory.CreateTempSubdirectory("openfarm-provision-progress-").FullName;

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
    public async Task ProvisionAsync_OnProgressSupplied_ReportsDownloadAndExtractStages()
    {
        var provisioner = new Provisioner(new FakeAssetDownloader([1, 2, 3]), new FakeAssetExtractor());
        var lockfile = Lockfile.CreateEmpty();
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.2.0", Platform.Linux, "https://example.test/asset.AppImage");
        var messages = new List<string>();

        await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None, onProgress: messages.Add);

        messages.Should().Contain(m => m.Contains("Downloading"));
        messages.Should().Contain(m => m.Contains("Extracting"));
        messages.Should().Contain(m => m.Contains("Provisioned"));
        messages.Should().OnlyContain(m => m.Contains("OrcaSlicer") && m.Contains("2.2.0") && m.Contains("Linux"));
    }

    [Fact]
    public async Task ProvisionAsync_AlreadyPresent_ReportsSkippedStageOnly()
    {
        var provisioner = new Provisioner(new FakeAssetDownloader([1, 2, 3]), new FakeAssetExtractor());
        var lockfile = Lockfile.CreateEmpty();
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.2.0", Platform.Linux, "https://example.test/asset.AppImage");
        await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None);

        var messages = new List<string>();
        await provisioner.ProvisionAsync(request, _outputRoot, lockfile, CancellationToken.None, onProgress: messages.Add);

        messages.Should().ContainSingle(m => m.Contains("Already present"));
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
