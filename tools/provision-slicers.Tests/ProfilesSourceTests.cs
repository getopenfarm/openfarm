using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;

namespace OpenFarm.Tools.ProvisionSlicers.Tests;

/// <summary>
/// A build's <c>profilesUrl</c> (OrcaSlicer 2.5+ ships its presets as caches only): the source tarball is pinned in
/// the lockfile like the binary, and only its <c>resources/profiles</c> JSONs reach the leaf's <c>profiles</c> directory.
/// </summary>
public class ProfilesSourceTests : IDisposable
{
    private const string AssetUrl = "https://example.test/OrcaSlicer.AppImage";
    private const string ProfilesUrl = "https://example.test/v2.5.1.tar.gz";

    private readonly string _root = Directory.CreateTempSubdirectory("openfarm-provision-profiles-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ProvisionAsync_WithProfilesUrl_RecordsBothDigestsAndRefusesAProfilesMismatch()
    {
        byte[] asset = [1, 2, 3], profiles = [4, 5, 6];
        var downloader = new UrlDownloader(new() { [AssetUrl] = asset, [ProfilesUrl] = profiles });
        var lockfile = Lockfile.CreateEmpty();
        var request = new ProvisionEntryRequest(Engine.OrcaSlicer, "2.5.1", Platform.Linux, AssetUrl, ProfilesUrl);

        var result = await new Provisioner(downloader, new RecordingExtractor()).ProvisionAsync(request, _root, lockfile, CancellationToken.None);

        result.Outcome.Should().Be(ProvisionOutcome.Provisioned);
        var entry = lockfile.Find(Engine.OrcaSlicer, "2.5.1", Platform.Linux)!;
        entry.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(asset)));
        entry.ProfilesUrl.Should().Be(ProfilesUrl);
        entry.ProfilesSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(profiles)));
        File.Exists(Path.Combine(_root, "orcaslicer", "2.5.1", "linux", "profiles", "v2.5.1.tar.gz")).Should().BeTrue();

        // Same lock, a different tarball: the leaf is not "already present" once removed, so the profiles digest is checked.
        Directory.Delete(Path.Combine(_root, "orcaslicer"), recursive: true);
        var tampered = new UrlDownloader(new() { [AssetUrl] = asset, [ProfilesUrl] = [9, 9] });

        var second = await new Provisioner(tampered, new RecordingExtractor()).ProvisionAsync(request, _root, lockfile, CancellationToken.None);

        second.Outcome.Should().Be(ProvisionOutcome.Failed);
        second.Message.Should().Contain("Profiles").And.Contain("mismatch");
    }

    [Fact]
    public async Task ExtractAsync_SourceTarball_WritesOnlyProfileJsonsWithoutThePrefix()
    {
        var tarball = TarGz(
            ("OrcaSlicer-2.5.1/resources/profiles/BBL.json", "{}"),
            ("OrcaSlicer-2.5.1/resources/profiles/BBL/machine/a.json", "{\"a\":1}"),
            ("OrcaSlicer-2.5.1/resources/profiles/BBL/bed.stl", "solid"),
            ("OrcaSlicer-2.5.1/src/OrcaSlicer.cpp", "int main"));

        await new DefaultAssetExtractor().ExtractAsync(tarball, "v2.5.1.tar.gz", _root, CancellationToken.None);

        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/'))
            .Should().BeEquivalentTo("BBL.json", "BBL/machine/a.json");
        File.ReadAllText(Path.Combine(_root, "BBL", "machine", "a.json")).Should().Be("{\"a\":1}");
    }

    [Fact]
    public async Task ExtractAsync_SourceTarballEntryEscapingTheDestination_IsRefused()
    {
        var tarball = TarGz(("OrcaSlicer-2.5.1/resources/profiles/../../../evil.json", "{}"));

        var act = () => new DefaultAssetExtractor().ExtractAsync(tarball, "v2.5.1.tar.gz", Path.Combine(_root, "dest"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside the destination*");
        File.Exists(Path.Combine(_root, "evil.json")).Should().BeFalse();
    }

    private static byte[] TarGz(params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip))
        {
            foreach (var (name, content) in files)
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
        }

        return output.ToArray();
    }

    private sealed class UrlDownloader(Dictionary<string, byte[]> byUrl) : IAssetDownloader
    {
        public Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken) => Task.FromResult(byUrl[url]);
    }

    /// <summary>Writes each asset under its own file name, so the test sees where each one went.</summary>
    private sealed class RecordingExtractor : IAssetExtractor
    {
        public Task ExtractAsync(byte[] assetBytes, string assetFileName, string destinationLeafDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destinationLeafDirectory);
            File.WriteAllBytes(Path.Combine(destinationLeafDirectory, assetFileName), assetBytes);
            return Task.CompletedTask;
        }
    }
}
