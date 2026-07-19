using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Invokes the configured headless OrcaSlicer/Bambu Studio CLI binary via
/// <see cref="Process"/> against a temp working directory (research.md §4). The actual toolpath is
/// never persisted (FR-005) — only the resulting estimate/thumbnail are read back, then the temp
/// output is deleted. Developed against <see cref="FakeSlicerEngine"/> first; real-CLI argument
/// shape validated manually against an actual OrcaSlicer install (infra/docker/api.Dockerfile), never
/// a CI gate — see quickstart.md.
/// </summary>
public sealed class CliSlicerEngine(IOptions<SlicerOptions> options) : ISlicerEngine
{
    public async Task<SlicingResult> SliceAsync(
        SlicerEngine engine, Stream modelFileStream, string materialType, decimal nozzleDiameterMm, CancellationToken cancellationToken)
    {
        var workDirectory = Directory.CreateTempSubdirectory("openfarm-slice-");
        try
        {
            var inputPath = Path.Combine(workDirectory.FullName, "model.3mf");
            await using (var inputFile = File.Create(inputPath))
            {
                await modelFileStream.CopyToAsync(inputFile, cancellationToken);
            }

            var outputPath = Path.Combine(workDirectory.FullName, "output.gcode");
            var cliPath = options.Value.CliPathFor(engine);

            var arguments = $"--slice 0 --load-filaments \"{EscapeMaterial(materialType)}\" " +
                $"--nozzle-diameter {nozzleDiameterMm.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"--export-gcode \"{outputPath}\" \"{inputPath}\"";

            var startInfo = new ProcessStartInfo(cliPath, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo)
                ?? throw new SlicingFailedException($"Could not start the {engine} CLI process.");

            using var timeoutCts = new CancellationTokenSource(options.Value.CliTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                TryKill(process);
                throw new SlicingFailedException($"The {engine} CLI timed out after {options.Value.CliTimeout}.");
            }

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                throw new SlicingFailedException($"The {engine} CLI exited with code {process.ExitCode}: {stderr}");
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

    private static string EscapeMaterial(string materialType) => materialType.Replace("\"", "\\\"");
}
