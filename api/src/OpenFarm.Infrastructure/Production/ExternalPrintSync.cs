using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Deducts filament for prints the printer started itself (Bambu Studio, the Handy app, the touchscreen, an SD card).
/// Once a print is seen running with no OpenFarm job behind it, the printed file is read back from the printer and its
/// per-tool grams and layer curve are frozen on an <see cref="ExternalPrint"/>; the trays the printer reports active are
/// recorded as they appear. <c>JobTelemetrySync</c> drives the print's later transitions and deducts through the same
/// <see cref="JobFilamentAutoDeduction"/> a dispatched job uses, so the accuracy is the same. Every failure here (no secret, file not found, unreadable) skips quietly: external
/// prints are best effort and must never wedge telemetry handling.
/// </summary>
public sealed partial class ExternalPrintSync(
    IExternalPrintRepository prints,
    IPrintJobRepository jobs,
    IConnectionSecretRepository secrets,
    ISecretProtector secretProtector,
    IPrinterConnectorFactory connectorFactory,
    FeedTrayResolver trayResolver,
    IActivityLogWriter activityLog,
    IProductionEventPublisher publisher,
    ILogger<ExternalPrintSync> logger)
{
    // An upper bound only: the printer's FTPS server is slow, and FluentFTP's own read timeouts end a stalled transfer sooner.
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(5);

    /// <summary>One print file being read back from a printer, started once per printer and job name.</summary>
    private sealed class Fetch(string jobName, Task<byte[]?> file)
    {
        public string JobName { get; } = jobName;

        public Task<byte[]?> File { get; } = file;

        public bool Used { get; init; }
    }

    // ponytail: process-local, so a restart mid-print retries the fetch once; persist an attempt marker if that ever matters.
    private static readonly ConcurrentDictionary<Guid, Fetch> Fetches = new();

    private static readonly Regex PlateName = PlateRegex();

    /// <summary>Run after the printer's telemetry is recorded: opens an external print on first sight of one running, and tracks trays.</summary>
    public async Task ObserveTelemetryAsync(Printer printer, CancellationToken cancellationToken)
    {
        if (printer.ReportedState?.ToUpperInvariant() is not ("RUNNING" or "PAUSE")) return;

        var open = await prints.GetOpenByPrinterIdAsync(printer.Id, cancellationToken);
        if (open is null)
        {
            if (string.IsNullOrWhiteSpace(printer.CurrentJobName)
                || await jobs.GetActiveJobByDispatchedPrinterIdAsync(printer.Id, cancellationToken) is not null) return;
            if (!Fetches.TryGetValue(printer.Id, out var fetch) || fetch.JobName != printer.CurrentJobName)
            {
                // The download runs beside the telemetry loop: reading the file is slow, and a printer must keep reporting meanwhile.
                Fetches[printer.Id] = await BeginFetchAsync(printer, cancellationToken)
                    ?? new Fetch(printer.CurrentJobName, Task.FromResult<byte[]?>(null)) { Used = true };
                return;
            }

            if (!fetch.File.IsCompleted || fetch.Used) return;
            // Drop the file bytes once taken; the entry stays so this print is not fetched again.
            Fetches[printer.Id] = new Fetch(fetch.JobName, Task.FromResult<byte[]?>(null)) { Used = true };
            open = fetch.File.Result is { } file ? await TryStartAsync(printer, file, cancellationToken) : null;
            if (open is null) return;
        }

        var observed = false;
        foreach (var slot in printer.Extruders.Select(e => e.ActiveSlot).OfType<FeedSlotRef>())
        {
            if (open.WantsSpoolFor(slot))
                observed |= open.ObserveSlot(slot, await trayResolver.ResolveSpoolAsync(printer.Id, slot, cancellationToken));
        }

        if (observed) await prints.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Forgets that the file for the printer's current print was fetched, so the next print is looked up afresh.</summary>
    public static void ClearAttempt(Guid printerId) => Fetches.TryRemove(printerId, out _);

    /// <summary>Starts reading the print's file in the background; null when the printer has no stored access code.</summary>
    private async Task<Fetch?> BeginFetchAsync(Printer printer, CancellationToken cancellationToken)
    {
        try
        {
            var secret = await secrets.GetByPrinterSerialNumberAsync(printer.SerialNumber, cancellationToken);
            if (secret is null) return null;
            var details = new PrinterConnectionDetails(
                printer.Brand, printer.ModelCode, printer.IpAddress, printer.SerialNumber, secretProtector.Unprotect(secret.EncryptedAccessCode));
            var connector = connectorFactory.CreateConnector(details);
            var jobName = printer.CurrentJobName!;
            var gcodeFile = printer.CurrentGcodeFile;
            var printerId = printer.Id;
            return new Fetch(jobName, Task.Run(() => FetchFileAsync(connector, details, printerId, jobName, gcodeFile, cancellationToken), CancellationToken.None));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogStartFailed(logger, printer.Id, ex);
            return null;
        }
    }

    /// <summary>Network only, no scoped services: it outlives the telemetry event that started it. Failures log once and yield null.</summary>
    private async Task<byte[]?> FetchFileAsync(
        IPrinterConnector connector, PrinterConnectionDetails details, Guid printerId, string jobName, string? gcodeFile,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FetchTimeout);
            var file = await connector.FetchPrintFileAsync(details, jobName, gcodeFile, timeout.Token);
            if (file is null) LogFileNotFound(logger, printerId, jobName);
            return file;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFetchFailed(logger, printerId, jobName, (int)Stopwatch.GetElapsedTime(started).TotalSeconds, ex.GetType().Name, ex.Message);
            return null;
        }
    }

    private async Task<ExternalPrint?> TryStartAsync(Printer printer, byte[] file, CancellationToken cancellationToken)
    {
        try
        {
            var named = PlateName.Match(printer.CurrentGcodeFile ?? "") is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
            if (JobFilamentUsageReader.ChoosePlate(file, named) is not { } plate)
            {
                LogPlateUnknown(logger, printer.Id, printer.CurrentJobName!, string.Join(", ", JobFilamentUsageReader.PlatesWithGCode(file)));
                return null;
            }

            var usage = JobFilamentUsageReader.Read(file, plate);
            if (usage.Usages.Count == 0)
            {
                LogNoUsage(logger, printer.Id, printer.CurrentJobName!, plate);
                return null;
            }

            var print = ExternalPrint.Start(printer.Id, printer.CurrentJobName!, usage.Usages.Select(u =>
                new ExternalFilamentSpec(u.ToolIndex, u.MaterialType, u.ColorHex, u.Grams,
                    usage.Extrusion.Tools.TryGetValue(u.ToolIndex, out var tool) ? FilamentUsageCurve.FromExtrusion(usage.Extrusion.Marks, [tool]) : null)));
            await prints.AddAsync(print, cancellationToken);
            await prints.SaveChangesAsync(cancellationToken);
            await activityLog.WriteAsync(ActivityLogEntry.Create(
                "ExternalPrint", print.Id.ToString(), "ExternalPrintStarted", null, ActorType.System), cancellationToken);
            await publisher.PrintJobStatusChangedAsync(print.Id, print.Status, cancellationToken);
            return print;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogStartFailed(logger, printer.Id, ex);
            return null;
        }
    }

    [GeneratedRegex(@"plate_(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlateRegex();

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {PrinterId} is printing '{JobName}' but its file could not be read back; no filament will be deducted.")]
    private static partial void LogFileNotFound(ILogger logger, Guid printerId, string jobName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {PrinterId} is printing '{JobName}', but its file holds plates {Plates} and the printed one is unknown; no filament will be deducted.")]
    private static partial void LogPlateUnknown(ILogger logger, Guid printerId, string jobName, string plates);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {PrinterId} is printing '{JobName}', but its file has no filament usage for plate {Plate}; no filament will be deducted.")]
    private static partial void LogNoUsage(ILogger logger, Guid printerId, string jobName, int plate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading '{JobName}' back from printer {PrinterId} failed after {Seconds}s ({ErrorType}: {Reason}); no filament will be deducted.")]
    private static partial void LogFetchFailed(ILogger logger, Guid printerId, string jobName, int seconds, string errorType, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start tracking the external print on printer {PrinterId}.")]
    private static partial void LogStartFailed(ILogger logger, Guid printerId, Exception exception);
}
