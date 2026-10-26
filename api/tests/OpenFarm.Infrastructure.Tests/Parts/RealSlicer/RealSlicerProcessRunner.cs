using System.Diagnostics;
using System.IO.Compression;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Parts.RealSlicer;

/// <summary>
/// Drives one real slice independently of <see cref="CliSlicerEngine"/>, which discards the raw
/// G-code after parsing metadata from it. Reuses <see cref="CliSlicerEngine"/>'s internal,
/// already-test-visible input file name helper rather than duplicating it, but runs its own process
/// so the raw G-code text can be captured.
/// </summary>
internal static class RealSlicerProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static async Task<string> SliceAsync(
        AvailableSlicer slicer, ISlicerInvocation invocation, byte[] modelBytes, PartFileFormat modelFormat,
        string materialType, decimal nozzleDiameterMm, ResolvedSlicingProfiles? resolvedProfiles, CancellationToken cancellationToken)
    {
        var workDir = Directory.CreateTempSubdirectory("openfarm-real-slicer-test-");
        try
        {
            var inputPath = Path.Combine(workDir.FullName, CliSlicerEngine.ResolveInputFileName(modelFormat));
            await File.WriteAllBytesAsync(inputPath, modelBytes, cancellationToken);

            var processInvocation = invocation.BuildInvocation(
                slicer, inputPath, materialType, nozzleDiameterMm, workDir.FullName, resolvedProfiles);

            var startInfo = new ProcessStartInfo(processInvocation.FileName, processInvocation.Arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workDir.FullName,
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start {slicer.Engine} CLI at '{processInvocation.FileName}'.");

            using var timeoutCts = new CancellationTokenSource(Timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"{slicer.Engine} {slicer.Version} CLI timed out after {Timeout}.");
            }

            if (process.ExitCode != 0 || !File.Exists(processInvocation.OutputPath))
            {
                var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                throw new InvalidOperationException(
                    $"{slicer.Engine} {slicer.Version} CLI exited with code {process.ExitCode} (empty stderr often means a " +
                    "native crash, e.g. exit 139/SIGSEGV — check slicers.manifest.json's pinned-version notes before " +
                    $"assuming this is a real regression): {stderr}");
            }

            return processInvocation.ProducesGCodeBearingThreeMf
                ? await ReadOnlyPlateGCodeAsync(processInvocation.OutputPath, cancellationToken)
                : await File.ReadAllTextAsync(processInvocation.OutputPath, cancellationToken);
        }
        finally
        {
            try
            {
                workDir.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup — a leftover temp dir is not worth failing the test over.
            }
        }
    }

    // The fixtures are single-plate projects, so the exported 3MF holds exactly one plate's G-code.
    private static async Task<string> ReadOnlyPlateGCodeAsync(string threeMfPath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(threeMfPath, cancellationToken);
        var plate = JobFilamentUsageReader.PlatesWithGCode(bytes).Single();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry($"Metadata/plate_{plate}.gcode")!.Open());
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
