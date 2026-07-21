using FluentAssertions;

namespace OpenFarm.Tools.ProvisionSlicers.Tests;

/// <summary>
/// Real <c>.dmg</c> extraction needs <c>hdiutil</c>, which only exists on macOS — that path is
/// manual-only, never a CI gate (same carve-out as the real Bambu LAN connector/physical hardware; CI
/// runs on Linux, so <see cref="OperatingSystem.IsMacOS"/> is always false here). What <em>is</em>
/// deterministic and worth a CI-gated test is the non-macOS fallback: the raw asset must still be
/// written (nothing downloaded is silently lost) and the outcome must be an honest, actionable failure
/// rather than a false-positive "Provisioned".
/// </summary>
public class DmgExtractionTests : IDisposable
{
    private readonly string _leaf = Directory.CreateTempSubdirectory("openfarm-provision-dmg-test-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_leaf, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ExtractAsync_DmgOnNonMacOsHost_WritesRawAssetAndThrowsActionableError()
    {
        if (OperatingSystem.IsMacOS())
        {
            // This test asserts the non-macOS fallback path only; CI (Linux) always takes it. On an
            // actual Mac, the real hdiutil path runs instead — manual-only, never a CI gate.
            return;
        }

        var extractor = new DefaultAssetExtractor();
        var assetBytes = new byte[] { 1, 2, 3, 4 };

        Func<Task> act = () => extractor.ExtractAsync(assetBytes, "OrcaSlicer_Mac_arm64_V2.2.0.dmg", _leaf, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("hdiutil").And.Contain("macOS");

        File.Exists(Path.Combine(_leaf, "unextracted.dmg")).Should().BeTrue();
        (await File.ReadAllBytesAsync(Path.Combine(_leaf, "unextracted.dmg"))).Should().BeEquivalentTo(assetBytes);
    }
}
