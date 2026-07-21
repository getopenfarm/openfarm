using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Regression coverage for the bug reported against a real (non-Docker) dev machine: a missing CLI
// binary must surface as SlicingFailedException (so RunEstimateContinuationAsync can mark the
// job/part failed) rather than an uncaught Win32Exception that the background service's generic
// catch silently swallows, leaving the job stuck in "Resolving" forever with no user-facing error.
public class CliSlicerEngineTests
{
    [Fact]
    public async Task SliceAsync_CliBinaryDoesNotExist_ThrowsSlicingFailedException()
    {
        var options = Options.Create(new SlicerOptions
        {
            OrcaSlicerCliPath = "/path/does/not/exist/orca-slicer-cli",
        });
        var engine = new CliSlicerEngine(options);
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceAsync(
            SlicerEngine.OrcaSlicer, modelStream, "Generic PLA", 0.4m, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingFailedException>())
            .WithMessage("*Could not start the OrcaSlicer CLI*");
    }

    // The Process.Start-specific catch above doesn't cover exceptions thrown before it — e.g.
    // SlicerOptions.CliPathFor's InvalidOperationException when BambuStudioCliPath is unset (its
    // default). Without the outer catch in SliceAsync, this escaped as a raw, unwrapped exception
    // that bypassed RunEstimateContinuationAsync's catch (SlicingFailedException) entirely, leaving
    // the job/part stuck in "Resolving" forever — same failure shape as the missing-CLI case above,
    // just reached via a different exception source.
    [Fact]
    public async Task SliceAsync_BambuStudioCliPathNotConfigured_ThrowsSlicingFailedException()
    {
        var options = Options.Create(new SlicerOptions());
        var engine = new CliSlicerEngine(options);
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceAsync(
            SlicerEngine.BambuStudio, modelStream, "Bambu PLA Basic", 0.4m, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingFailedException>())
            .WithMessage("*Slicing failed*BambuStudioCliPath*");
    }
}
