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
}
