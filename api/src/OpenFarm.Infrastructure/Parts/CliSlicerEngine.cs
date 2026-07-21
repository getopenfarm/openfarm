using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Invokes the resolved <see cref="AvailableSlicer"/>'s headless CLI binary via <see cref="Process"/>
/// against a temp working directory (research.md §4). Resolves the entry from
/// <see cref="IAvailableSlicerCatalog"/> and delegates argument-building to the
/// <see cref="ISlicerInvocation"/> strategy selected by engine — the orchestration itself (temp dir,
/// process lifetime, timeout, failure → <see cref="SlicingFailedException"/>) is unchanged from the
/// single-version Phase 4 implementation. The actual toolpath is never persisted (FR-005) — only the
/// resulting estimate/thumbnail are read back, then the temp output is deleted. Developed against
/// <see cref="FakeSlicerEngine"/> first; real-CLI argument shape validated manually against actual
/// slicer installs (infra/docker/api.Dockerfile), never a CI gate — see quickstart.md.
/// </summary>
public sealed class CliSlicerEngine(
    IAvailableSlicerCatalog catalog,
    IHostPlatformProvider hostPlatform,
    IEnumerable<ISlicerInvocation> invocationStrategies,
    IOptions<SlicerOptions> options) : ISlicerEngine
{
    public async Task<SlicingResult> SliceAsync(
        SlicerRef slicer, Stream modelFileStream, string materialType, decimal nozzleDiameterMm, CancellationToken cancellationToken)
    {
        try
        {
            return await SliceCoreAsync(slicer, modelFileStream, materialType, nozzleDiameterMm, cancellationToken);
        }
        catch (Exception ex) when (ex is not SlicingFailedException && ex is not OperationCanceledException && ex is not SlicerUnavailableException)
        {
            // Closes the rest of the exception surface the Process.Start-specific catch below
            // doesn't cover — temp-dir/file I/O errors or unparseable output must surface as
            // SlicingFailedException too, or it escapes RunEstimateContinuationAsync's catch and
            // leaves the job/part silently stuck in "Resolving" forever (research.md §4).
            // Genuine caller-requested cancellation, and an availability mismatch (which should have
            // already been caught upstream by ResolveSlicerSelection), are deliberately left
            // unconverted so they're still recognizable as what they are.
            throw new SlicingFailedException($"Slicing failed: {ex.Message}", ex);
        }
    }

    private async Task<SlicingResult> SliceCoreAsync(
        SlicerRef slicer, Stream modelFileStream, string materialType, decimal nozzleDiameterMm, CancellationToken cancellationToken)
    {
        var platform = hostPlatform.Current;
        var available = catalog.Find(slicer, platform)
            ?? throw new SlicerUnavailableException(slicer.Engine, slicer.Version, platform);

        var invocationStrategy = invocationStrategies.FirstOrDefault(s => s.SupportsEngine(slicer.Engine))
            ?? throw new SlicingFailedException($"No CLI invocation strategy is registered for {slicer.Engine}.");

        var workDirectory = Directory.CreateTempSubdirectory("openfarm-slice-");
        try
        {
            var inputPath = Path.Combine(workDirectory.FullName, "model.3mf");
            await using (var inputFile = File.Create(inputPath))
            {
                await modelFileStream.CopyToAsync(inputFile, cancellationToken);
            }

            var outputPath = Path.Combine(workDirectory.FullName, "output.gcode");
            var processInvocation = invocationStrategy.BuildInvocation(available, inputPath, materialType, nozzleDiameterMm, outputPath);

            var startInfo = new ProcessStartInfo(processInvocation.FileName, processInvocation.Arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new SlicingFailedException($"Could not start the {slicer.Engine} CLI process.");
            }
            catch (Exception ex) when (ex is not SlicingFailedException)
            {
                // Most commonly Win32Exception when the bundled binary is missing/broken on this
                // host — surfaced as SlicingFailedException so callers (RunEstimateContinuationAsync)
                // mark the job/part failed instead of this escaping to the background service's
                // generic catch, which only logs and leaves the job silently stuck (research.md §4).
                throw new SlicingFailedException($"Could not start the {slicer.Engine} CLI at '{processInvocation.FileName}': {ex.Message}", ex);
            }

            using (process)
            {
                using var timeoutCts = new CancellationTokenSource(options.Value.CliTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    TryKill(process);
                    throw new SlicingFailedException($"The {slicer.Engine} CLI timed out after {options.Value.CliTimeout}.");
                }

                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                    throw new SlicingFailedException($"The {slicer.Engine} CLI exited with code {process.ExitCode}: {stderr}");
                }

                var gcode = await File.ReadAllTextAsync(outputPath, cancellationToken);
                var values = GCodeMetadataHelper.ParseComments(gcode);
                var estimatedMinutes = GCodeMetadataHelper.ParseEstimatedPrintTimeMinutes(values)
                    ?? throw new SlicingFailedException("The sliced G-code did not contain a parseable estimated print time.");
                var estimatedWeight = GCodeMetadataHelper.ParseFilamentWeightGrams(values)
                    ?? throw new SlicingFailedException("The sliced G-code did not contain a parseable filament weight.");
                var thumbnail = GCodeMetadataHelper.ExtractEmbeddedThumbnailPng(gcode);

                return new SlicingResult(
                    PlateCount: 1,
                    estimatedMinutes,
                    estimatedWeight,
                    thumbnail is null ? [] : [new ThumbnailSpec(0, thumbnail)]);
            }
        }
        finally
        {
            try
            {
                workDirectory.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup — a leftover temp dir is not worth failing the slice result over.
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout firing and this call.
        }
    }
}
