using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

public class BundledSlicerScannerTests : IDisposable
{
    private readonly string _root;

    public BundledSlicerScannerTests()
    {
        _root = Directory.CreateTempSubdirectory("openfarm-slicer-bundle-tests-").FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Scan_EmptyRoot_ReturnsNoEntries()
    {
        var scanner = CreateScanner();

        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_RootDoesNotExist_ReturnsNoEntries()
    {
        var scanner = new BundledSlicerScanner(
            Options.Create(new SlicerBundleOptions { RootDirectory = Path.Combine(_root, "does-not-exist") }),
            NullLogger<BundledSlicerScanner>.Instance);

        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_RootDoesNotExist_LogsWarning()
    {
        var missingRoot = Path.Combine(_root, "does-not-exist");
        var logger = new RecordingLogger<BundledSlicerScanner>();
        var scanner = new BundledSlicerScanner(Options.Create(new SlicerBundleOptions { RootDirectory = missingRoot }), logger);

        scanner.Scan();

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains(missingRoot));
    }

    [Fact]
    public void Scan_RootExistsButHasNoLeaves_LogsWarning()
    {
        var logger = new RecordingLogger<BundledSlicerScanner>();
        var scanner = new BundledSlicerScanner(Options.Create(new SlicerBundleOptions { RootDirectory = _root }), logger);

        scanner.Scan();

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains(_root));
    }

    [Fact]
    public void Scan_OneLeaf_ReturnsOneAvailableSlicer()
    {
        CreateLeaf("orcaslicer", "2.2.0", "linux");
        var scanner = CreateScanner();

        var result = scanner.Scan();

        result.Should().ContainSingle();
        result[0].Engine.Should().Be(SlicerEngine.OrcaSlicer);
        result[0].Version.Should().Be("2.2.0");
        result[0].Platform.Should().Be(SlicerPlatform.Linux);
        result[0].ExecutablePath.Should().EndWith("AppRun");
    }

    [Fact]
    public void Scan_MultipleEnginesAndVersions_ReturnsAllOfThem()
    {
        CreateLeaf("orcaslicer", "2.2.0", "linux");
        CreateLeaf("orcaslicer", "2.3.0", "linux");
        CreateLeaf("superslicer", "2.5.59.13", "linux");
        var scanner = CreateScanner();

        var result = scanner.Scan();

        result.Should().HaveCount(3);
        result.Select(a => (a.Engine, a.Version)).Should().Contain(
        [
            (SlicerEngine.OrcaSlicer, "2.2.0"),
            (SlicerEngine.OrcaSlicer, "2.3.0"),
            (SlicerEngine.SuperSlicer, "2.5.59.13"),
        ]);
    }

    [Fact]
    public void Scan_DuplicateLeafViaDifferentlyCasedEngineSlug_DeduplicatesDeterministically()
    {
        // Two physically distinct directories that both parse as the same (engine, version, platform)
        // triple — the scanner must keep exactly one, chosen by stable path ordering.
        CreateLeaf("OrcaSlicer", "2.2.0", "linux");
        CreateLeaf("orcaslicer", "2.2.0", "linux");
        var scanner = CreateScanner();

        var result = scanner.Scan();

        result.Should().ContainSingle(a => a.Engine == SlicerEngine.OrcaSlicer && a.Version == "2.2.0");
    }

    [Fact]
    public void Scan_UnrecognizedPlatformSlug_IsFilteredOut()
    {
        CreateLeaf("orcaslicer", "2.2.0", "freebsd");
        var scanner = CreateScanner();

        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_UnrecognizedEngineSlug_IsFilteredOut()
    {
        CreateLeaf("unknownengine", "2.2.0", "linux");
        var scanner = CreateScanner();

        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    [Fact]
    public void ScanLeafDirectories_ReturnsOneLeafPerVersion_PreferringTheGivenPlatform()
    {
        CreateLeaf("orcaslicer", "2.3.1", "macos");
        CreateLeaf("orcaslicer", "2.3.1", "linux");
        CreateLeaf("orcaslicer", "2.4.2", "macos"); // no Linux copy of this version: falls back to what exists
        CreateLeaf("bambustudio", "2.7.1", "linux");
        var scanner = CreateScanner();

        var leaves = scanner.ScanLeafDirectories(SlicerPlatform.Linux);

        leaves.Select(leaf => Path.GetRelativePath(_root, leaf).Replace('\\', '/')).Should().Equal(
            "bambustudio/2.7.1/linux", "orcaslicer/2.3.1/linux", "orcaslicer/2.4.2/macos");
    }

    [Fact]
    public void ScanLeafDirectories_RootDoesNotExist_ReturnsNothing()
    {
        var scanner = new BundledSlicerScanner(
            Options.Create(new SlicerBundleOptions { RootDirectory = Path.Combine(_root, "does-not-exist") }),
            NullLogger<BundledSlicerScanner>.Instance);

        scanner.ScanLeafDirectories(SlicerPlatform.Linux).Should().BeEmpty();
    }

    private BundledSlicerScanner CreateScanner() =>
        new(Options.Create(new SlicerBundleOptions { RootDirectory = _root }), NullLogger<BundledSlicerScanner>.Instance);

    private void CreateLeaf(string engineSlug, string version, string platformSlug)
    {
        var leaf = Path.Combine(_root, engineSlug, version, platformSlug);
        Directory.CreateDirectory(leaf);
        File.WriteAllText(Path.Combine(leaf, "AppRun"), "#!/bin/sh\nexit 0\n");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
