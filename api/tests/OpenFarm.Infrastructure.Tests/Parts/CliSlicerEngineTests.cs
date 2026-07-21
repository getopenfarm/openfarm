using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
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

    [Fact]
    public async Task SliceAsync_SlicerNotInCatalog_ThrowsSlicerUnavailableException()
    {
        var engine = CreateEngine(new FakeAvailableSlicerCatalog());
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceAsync(Orca220, modelStream, "Generic PLA", 0.4m, CancellationToken.None);

        await act.Should().ThrowAsync<SlicerUnavailableException>();
    }

    [Fact]
    public async Task SliceAsync_BundledExecutableDoesNotExist_ThrowsSlicingFailedException()
    {
        var catalog = new FakeAvailableSlicerCatalog(
            new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/path/does/not/exist/orca-slicer-cli"));
        var engine = CreateEngine(catalog);
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceAsync(Orca220, modelStream, "Generic PLA", 0.4m, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingFailedException>())
            .WithMessage("*Could not start the OrcaSlicer CLI*");
    }

    private static CliSlicerEngine CreateEngine(FakeAvailableSlicerCatalog catalog) => new(
        catalog,
        new FakeHostPlatformProvider(SlicerPlatform.Linux),
        [new PrusaSliceFamilyInvocation(), new SuperSlicerInvocation()],
        Options.Create(new SlicerOptions()));

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
