using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;

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
public sealed partial class CliSlicerEngine(
    IAvailableSlicerCatalog catalog,
    IHostPlatformProvider hostPlatform,
    IEnumerable<ISlicerInvocation> invocationStrategies,
    ResolveSlicingProfileSelection profileSelection,
    IOptions<SlicerOptions> options,
    ILogger<CliSlicerEngine> logger) : ISlicerEngine
{
    public async Task<SlicingResult> EstimateAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        EstimationConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            return await SliceCoreAsync(slicer, modelFileStream, modelFileFormat, configuration.MaterialType ?? string.Empty,
                configuration.NozzleDiameterMm ?? 0, null, cancellationToken, configuration);
        }
        catch (Exception ex) when (ex is not SlicingFailedException && ex is not OperationCanceledException
            && ex is not SlicerUnavailableException && ex is not SlicingProfileUnconfiguredException)
        { throw new SlicingFailedException($"Estimation failed: {ex.Message}", ex); }
    }

    public async Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        string materialType, decimal nozzleDiameterMm, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken cancellationToken)
    {
        try { return await SliceCoreAsync(slicer, modelFileStream, modelFileFormat, materialType, nozzleDiameterMm, printerId, cancellationToken, productionProfiles: profiles); }
        catch (Exception ex) when (ex is not SlicingFailedException && ex is not OperationCanceledException
            && ex is not SlicerUnavailableException && ex is not SlicingProfileUnconfiguredException)
        { throw new SlicingFailedException($"Slicing failed: {ex.Message}", ex); }
    }

    public async Task<SlicingResult> SliceAsync(
        SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat, string materialType, decimal nozzleDiameterMm,
        Guid? printerId, CancellationToken cancellationToken)
    {
        try
        {
            return await SliceCoreAsync(slicer, modelFileStream, modelFileFormat, materialType, nozzleDiameterMm, printerId, cancellationToken);
        }
        catch (Exception ex) when (ex is not SlicingFailedException && ex is not OperationCanceledException
            && ex is not SlicerUnavailableException && ex is not SlicingProfileUnconfiguredException)
        {
            // Closes the rest of the exception surface the Process.Start-specific catch below
            // doesn't cover — temp-dir/file I/O errors or unparseable output must surface as
            // SlicingFailedException too, or it escapes RunEstimateContinuationAsync's catch and
            // leaves the job/part silently stuck in "Resolving" forever (research.md §4).
            // Genuine caller-requested cancellation, an availability mismatch (which should have
            // already been caught upstream by ResolveSlicerSelection), and an unconfigured slicing
            // profile (ResolveSlicingProfileSelection) are deliberately left unconverted so they're
            // still recognizable as what they are, each with its own distinct ProblemDetails title.
            throw new SlicingFailedException($"Slicing failed: {ex.Message}", ex);
        }
    }

    private async Task<SlicingResult> SliceCoreAsync(
        SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat, string materialType, decimal nozzleDiameterMm,
        Guid? printerId, CancellationToken cancellationToken, EstimationConfiguration? estimation = null, ResolvedSlicingProfiles? productionProfiles = null)
    {
        var platform = hostPlatform.Current;
        var available = catalog.Find(slicer, platform)
            ?? throw new SlicerUnavailableException(slicer.Engine, slicer.Version, platform);

        var invocationStrategy = invocationStrategies.FirstOrDefault(s => s.SupportsEngine(slicer.Engine))
            ?? throw new SlicingFailedException($"No CLI invocation strategy is registered for {slicer.Engine}.");

        var workDirectory = Directory.CreateTempSubdirectory("openfarm-slice-");
        try
        {
            var inputPath = Path.Combine(workDirectory.FullName, ResolveInputFileName(modelFileFormat));
            await using (var inputFile = File.Create(inputPath))
            {
                await modelFileStream.CopyToAsync(inputFile, cancellationToken);
            }

            // Only OrcaSlicer/Bambu Studio need real, complete presets (PrusaSliceFamilyInvocation) —
            // SuperSlicer builds plain scalar flags and never looks at this.
            var resolvedProfiles = productionProfiles ?? (estimation is not null ? estimation.Profiles
                : slicer.Engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio
                    ? await profileSelection.ResolveAsync(printerId, materialType, narrowedFilamentProfileId: null, cancellationToken, slicer.Engine) : null);

            if (resolvedProfiles is not null)
            {
                LogResolvedSlicingProfiles(logger, slicer.Engine, resolvedProfiles.MachineProfileName, resolvedProfiles.ProcessProfileName, resolvedProfiles.FilamentProfileName);
            }

            if (estimation?.EmbeddedProject == true && modelFileFormat != PartFileFormat.ThreeMf)
                throw new SlicingFailedException("Embedded settings require a 3MF project.");
            var processInvocation = estimation?.EmbeddedProject == true
                ? PrusaSliceFamilyInvocation.BuildEmbeddedProjectInvocation(available, inputPath, workDirectory.FullName)
                : invocationStrategy.BuildInvocation(available, inputPath, materialType, nozzleDiameterMm, workDirectory.FullName, resolvedProfiles);

            var startInfo = new ProcessStartInfo(Path.GetFullPath(processInvocation.FileName), processInvocation.Arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workDirectory.FullName,
            };

            LogInvokingSlicer(logger, slicer.Engine, processInvocation.FileName, processInvocation.Arguments);

            Process process;
            var stopwatch = Stopwatch.StartNew();
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
                var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
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

                catch (OperationCanceledException)
                {
                    TryKill(process);
                    throw;
                }
                await stdout;
                var errorOutput = await stderr;
                stopwatch.Stop();
                LogSlicerExited(logger, slicer.Engine, process.ExitCode, stopwatch.ElapsedMilliseconds);

                if (process.ExitCode != 0 || !File.Exists(processInvocation.OutputPath))
                {
                    throw new SlicingFailedException($"The {slicer.Engine} CLI exited with code {process.ExitCode}: {errorOutput}");
                }

                if (processInvocation.ProducesGCodeBearingThreeMf)
                {
                    await using var output = File.OpenRead(processInvocation.OutputPath);
                    var metadata = await new AlreadySlicedFileMetadataParser().ParseAsync(output, PartFileFormat.ThreeMf, cancellationToken);
                    return new(metadata.PlateCount, metadata.EstimatedPrintTimeMinutes, metadata.EstimatedFilamentWeightGrams,
                        metadata.Thumbnails, await File.ReadAllBytesAsync(processInvocation.OutputPath, cancellationToken), PartFileFormat.ThreeMf);
                }
                var gcode = await File.ReadAllTextAsync(processInvocation.OutputPath, cancellationToken);
                var values = GCodeMetadataHelper.ParseComments(gcode);
                var thumbnail = GCodeMetadataHelper.ExtractEmbeddedThumbnailPng(gcode);
                return new SlicingResult(1, GCodeMetadataHelper.ParseEstimatedPrintTimeMinutes(values),
                    GCodeMetadataHelper.ParseFilamentWeightGrams(values),
                    thumbnail is null ? [] : [new ThumbnailSpec(0, thumbnail)], System.Text.Encoding.UTF8.GetBytes(gcode));
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

    /// <summary>
    /// OrcaSlicer/Bambu Studio's <c>--export-3mf</c> writes a gcode-bearing 3MF, not a plain .gcode
    /// file — the real G-code for the first (only, today) plate lives inside it as a zip entry.
    /// Internal (not private) so InternalsVisibleTo lets Infrastructure.Tests exercise this directly
    /// without a real slicer subprocess, same rationale as BambuCertificateTrust.
    /// </summary>
    internal static async Task<string> ReadEmbeddedPlateGCodeAsync(string threeMfPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(threeMfPath);
        var entry = archive.GetEntry("Metadata/plate_1.gcode")
            ?? throw new SlicingFailedException("The exported 3MF did not contain Metadata/plate_1.gcode.");

        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// OrcaSlicer/Bambu Studio dispatch model loading off the input file's extension, not its
    /// content — writing an STL upload to disk as "model.3mf" made the slicer try to unzip it as a
    /// 3MF and fail with "Loading of a model file failed." Internal (not private) for the same
    /// InternalsVisibleTo testability as <see cref="ReadEmbeddedPlateGCodeAsync"/>.
    /// </summary>
    internal static string ResolveInputFileName(PartFileFormat modelFileFormat) =>
        modelFileFormat == PartFileFormat.Stl ? "model.stl" : "model.3mf";

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invoking {Engine} CLI: {FileName} {Arguments}")]
    private static partial void LogInvokingSlicer(ILogger logger, SlicerEngine engine, string fileName, string arguments);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Engine} CLI exited with code {ExitCode} after {ElapsedMilliseconds}ms")]
    private static partial void LogSlicerExited(ILogger logger, SlicerEngine engine, int exitCode, long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Using slicing profiles for {Engine}: machine={MachineProfileName} process={ProcessProfileName} filament={FilamentProfileName}")]
    private static partial void LogResolvedSlicingProfiles(ILogger logger, SlicerEngine engine, string machineProfileName, string processProfileName, string filamentProfileName);
}
