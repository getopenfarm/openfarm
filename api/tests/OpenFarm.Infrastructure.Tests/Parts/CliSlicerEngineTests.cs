using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Regression coverage for the bug reported against a real (non-Docker) dev machine: a missing/broken
// CLI binary must surface as SlicingFailedException (so RunEstimateContinuationAsync can mark the
// job/part failed) rather than an uncaught Win32Exception that the background service's generic
// catch silently swallows, leaving the job stuck in "Resolving" forever with no user-facing error.
// Also covers the Phase 8 catalog-driven resolution path: an (engine, version) absent from the
// catalog throws SlicerUnavailableException instead of ever starting a process.
public class CliSlicerEngineTests
{
    private static readonly SlicerRef Orca220 = new(SlicerEngine.OrcaSlicer, "2.2.0");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SliceConfiguredAsync_PreservesExportedFileBytesAndFormat(bool project)
    {
        if (OperatingSystem.IsWindows()) return;
        var invocation = new ExportFixtureInvocation(project);
        var engine = new CliSlicerEngine(
            new FakeAvailableSlicerCatalog(new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/bin/sh")),
            new FakeHostPlatformProvider(SlicerPlatform.Linux), [invocation],
            Options.Create(new SlicerOptions()), NullLogger<CliSlicerEngine>.Instance);
        using var input = new MemoryStream([1, 2, 3]);

        var result = await engine.SliceConfiguredAsync(Orca220, input, PartFileFormat.Stl, "PLA", 0.4m, Guid.NewGuid(),
            TestResolvedProfiles.Create(Orca220), default);

        result.OutputFileBytes.Should().Equal(invocation.ExportedBytes);
        result.OutputFileFormat.Should().Be(project ? PartFileFormat.ThreeMf : PartFileFormat.GCode);
        if (project) result.Plates.Should().ContainSingle().Which.Number.Should().Be(1);
        else result.Plates.Should().BeNull();
    }

    private sealed class ExportFixtureInvocation(bool project) : ISlicerInvocation
    {
        public byte[] ExportedBytes { get; private set; } = [];
        public bool Built { get; private set; }
        public bool SupportsEngine(SlicerEngine engine) => true;

        public SlicerProcessInvocation BuildInvocation(AvailableSlicer slicer, string modelFilePath, string materialType,
            decimal nozzleDiameterMm, string workDirectoryPath, ResolvedSlicingProfiles? resolvedProfiles)
        {
            Built = true;
            var output = Path.Combine(workDirectoryPath, project ? "output.gcode.3mf" : "output.gcode");
            const string gcode = "; nozzle_diameter = 0.4\n; filament_type = PLA\nG28\n";
            if (project)
            {
                using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
                using (var plate = new StreamWriter(archive.CreateEntry("Metadata/plate_1.gcode").Open())) plate.Write(gcode);
                using var settings = new StreamWriter(archive.CreateEntry("Metadata/project_settings.config").Open());
                settings.Write("{}");
            }
            else
            {
                File.WriteAllText(output, gcode);
            }
            ExportedBytes = File.ReadAllBytes(output);
            return new(slicer.ExecutablePath, "-c true", output, project);
        }
    }

    [Fact]
    public async Task SliceConfiguredAsync_SlicerNotInCatalog_ThrowsSlicerUnavailableException()
    {
        var engine = CreateEngine(new FakeAvailableSlicerCatalog());
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceConfiguredAsync(Orca220, modelStream, PartFileFormat.Stl, "PLA", 0.4m,
            Guid.NewGuid(), TestResolvedProfiles.Create(Orca220), CancellationToken.None);

        await act.Should().ThrowAsync<SlicerUnavailableException>();
    }

    // Presets resolved for one slicer must never run in another: the operator chose them for that pin only.
    [Fact]
    public async Task SliceConfiguredAsync_ProfilesResolvedForAnotherSlicer_NeverBuildsAnInvocation()
    {
        var invocation = new ExportFixtureInvocation(project: false);
        var engine = new CliSlicerEngine(
            new FakeAvailableSlicerCatalog(new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/bin/sh")),
            new FakeHostPlatformProvider(SlicerPlatform.Linux), [invocation],
            Options.Create(new SlicerOptions()), NullLogger<CliSlicerEngine>.Instance);
        using var input = new MemoryStream([1, 2, 3]);
        var resolvedFor = new SlicerRef(SlicerEngine.BambuStudio, "2.7.1");

        var act = async () => await engine.SliceConfiguredAsync(Orca220, input, PartFileFormat.Stl, "PLA", 0.4m,
            Guid.NewGuid(), TestResolvedProfiles.Create(resolvedFor, format: ProfileSourceFormat.BambuStudio), default);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*resolved for BambuStudio 2.7.1, not OrcaSlicer 2.2.0*");
        invocation.Built.Should().BeFalse("no process is started with profiles resolved for another slicer");
    }

    [Fact]
    public async Task EstimateAsync_BundledExecutableDoesNotExist_ThrowsSlicingFailedException()
    {
        var catalog = new FakeAvailableSlicerCatalog(
            new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/path/does/not/exist/orca-slicer-cli"));
        var engine = CreateEngine(catalog);
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.EstimateAsync(Orca220, modelStream, PartFileFormat.ThreeMf,
            new EstimationConfiguration(true, null, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingFailedException>())
            .WithMessage("*Could not start the OrcaSlicer CLI*");
    }

    [Fact]
    public async Task EstimateAsync_RelativeExecutablePath_IsResolvedBeforeChangingWorkingDirectory()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("openfarm-relative-slicer-");
        try
        {
            var path = Path.Combine(directory.FullName, "AppRun");
            await File.WriteAllTextAsync(path, "#!/bin/sh\nexit 1\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var catalog = new FakeAvailableSlicerCatalog(new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux,
                Path.GetRelativePath(Environment.CurrentDirectory, path)));
            var engine = CreateEngine(catalog);
            using var input = new MemoryStream([1, 2, 3]);
            var act = async () => await engine.EstimateAsync(Orca220, input, PartFileFormat.ThreeMf,
                new EstimationConfiguration(true, null, null, null), CancellationToken.None);
            await act.Should().ThrowAsync<SlicingFailedException>().WithMessage("*exited with code 1*");
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task EstimateAsync_KnownCliExitCode_IsExplainedInTheFailureMessage()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("openfarm-exit-code-slicer-");
        try
        {
            var path = Path.Combine(directory.FullName, "AppRun");
            // 232 = CLI_FILE_VERSION_NOT_SUPPORTED (-24) as .NET sees a Unix exit status.
            await File.WriteAllTextAsync(path, "#!/bin/sh\necho 'version check failed' >&2\nexit 232\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var catalog = new FakeAvailableSlicerCatalog(new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, path));
            var engine = CreateEngine(catalog);
            using var input = new MemoryStream([1, 2, 3]);

            var act = async () => await engine.EstimateAsync(Orca220, input, PartFileFormat.ThreeMf,
                new EstimationConfiguration(true, null, null, null), CancellationToken.None);

            await act.Should().ThrowAsync<SlicingFailedException>()
                .WithMessage("*exited with code 232 (the 3MF was saved by a newer or incompatible slicer version)*version check failed*");
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(PartFileFormat.Stl, "model.stl")]
    [InlineData(PartFileFormat.ThreeMf, "model.3mf")]
    public void ResolveInputFileName_UsesTheRealExtensionSoTheSlicerDoesNotMisdetectTheFormat(PartFileFormat format, string expected)
    {
        CliSlicerEngine.ResolveInputFileName(format).Should().Be(expected);
    }

    private static CliSlicerEngine CreateEngine(FakeAvailableSlicerCatalog catalog) => new(
        catalog,
        new FakeHostPlatformProvider(SlicerPlatform.Linux),
        [new PrusaSliceFamilyInvocation(), new SuperSlicerInvocation()],
        Options.Create(new SlicerOptions()),
        NullLogger<CliSlicerEngine>.Instance);

    private sealed class FakeAvailableSlicerCatalog(params AvailableSlicer[] available) : IAvailableSlicerCatalog
    {
        public IReadOnlyList<AvailableSlicer> GetAvailable() => available;

        public AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform) =>
            available.FirstOrDefault(a =>
                a.Engine == slicerRef.Engine
                && string.Equals(a.Version, slicerRef.Version, StringComparison.Ordinal)
                && a.Platform == platform);
    }

    private sealed class FakeHostPlatformProvider(SlicerPlatform current) : IHostPlatformProvider
    {
        public SlicerPlatform Current { get; } = current;
    }
}
