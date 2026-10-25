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
/// Records and deducts filament for prints the printer started itself (Bambu Studio, the Handy app, the touchscreen, an SD
/// card). A print seen running with no OpenFarm job behind it becomes an <see cref="ExternalPrint"/> at once, so it counts
/// from first sight; its file is read back from the printer meanwhile, and its per-tool grams and layer curve become the
/// print's lines. The trays the printer reports active are recorded as they appear. <c>JobTelemetrySync</c> drives the
/// print's later transitions and deducts through the same <see cref="JobFilamentAutoDeduction"/> a dispatched job uses, so
/// the accuracy is the same. A file that can't be read (no secret, not found, unreadable) leaves one line of unknown grams,
/// which the operator follows up; nothing here may wedge telemetry handling.
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

    /// <summary>Run after the printer's telemetry is recorded: opens an external print on first sight of one running, gives it its file's lines, and tracks trays.</summary>
    public async Task ObserveTelemetryAsync(Printer printer, CancellationToken cancellationToken)
    {
        if (printer.ReportedState?.ToUpperInvariant() is not ("RUNNING" or "PAUSE")) return;

        var open = await prints.GetOpenByPrinterIdAsync(printer.Id, cancellationToken);
        if (open is null)
        {
            if (string.IsNullOrWhiteSpace(printer.CurrentJobName)
                || await jobs.GetActiveJobByDispatchedPrinterIdAsync(printer.Id, cancellationToken) is not null) return;
            open = await TryStartAsync(printer, cancellationToken);
            if (open is null) return;
        }

        if (open.Filaments.Count == 0) await AttachFileAsync(printer, open, cancellationToken);

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

    /// <summary>Opens the print the printer is running, before its file is read: a print whose file never reads still counts.</summary>
    private async Task<ExternalPrint?> TryStartAsync(Printer printer, CancellationToken cancellationToken)
    {
        try
        {
            var print = ExternalPrint.Start(printer.Id, printer.CurrentJobName!);
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

    /// <summary>
    /// Reads the open print's file in the background and, once it is read, gives the print a line per tool. A file that can't
    /// be read, or names no usage, gives it one line of unknown grams. After a restart mid-print the file is read again.
    /// </summary>
    private async Task AttachFileAsync(Printer printer, ExternalPrint open, CancellationToken cancellationToken)
    {
        if (!Fetches.TryGetValue(printer.Id, out var fetch) || fetch.JobName != open.Name)
        {
            // The download runs beside the telemetry loop: reading the file is slow, and a printer must keep reporting meanwhile.
            Fetches[printer.Id] = await BeginFetchAsync(printer, cancellationToken) ?? new Fetch(open.Name, Task.FromResult<byte[]?>(null));
            return;
        }

        if (!fetch.File.IsCompleted || fetch.Used) return;
        // Drop the file bytes once taken; the entry stays so this print is not fetched again.
        Fetches[printer.Id] = new Fetch(fetch.JobName, Task.FromResult<byte[]?>(null)) { Used = true };
        open.AttachFilaments(fetch.File.Result is { } file ? Usage(printer, file) : null);
        prints.TrackNewFilaments(open.Filaments);
        await prints.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The printed plate's grams and layer curve per tool, or null when the plate or its usage is unknown.</summary>
    private List<ExternalFilamentSpec>? Usage(Printer printer, byte[] file)
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

            return usage.Usages.Select(u =>
                new ExternalFilamentSpec(u.ToolIndex, u.MaterialType, u.ColorHex, u.Grams,
                    usage.Extrusion.Tools.TryGetValue(u.ToolIndex, out var tool) ? FilamentUsageCurve.FromExtrusion(usage.Extrusion.Marks, [tool]) : null))
                .ToList();
        }
        catch (Exception ex)
        {
            LogFileUnreadable(logger, printer.Id, printer.CurrentJobName!, ex);
            return null;
        }
    }

    [GeneratedRegex(@"plate_(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlateRegex();

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {PrinterId} is printing '{JobName}' but its file could not be read back; its filament is left for the operator.")]
    private static partial void LogFileNotFound(ILogger logger, Guid printerId, string jobName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {PrinterId} is printing '{JobName}', but its file holds plates {Plates} and the printed one is unknown; its filament is left for the operator.")]
    private static partial void LogPlateUnknown(ILogger logger, Guid printerId, string jobName, string plates);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {PrinterId} is printing '{JobName}', but its file has no filament usage for plate {Plate}; its filament is left for the operator.")]
    private static partial void LogNoUsage(ILogger logger, Guid printerId, string jobName, int plate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading '{JobName}' back from printer {PrinterId} failed after {Seconds}s ({ErrorType}: {Reason}); its filament is left for the operator.")]
    private static partial void LogFetchFailed(ILogger logger, Guid printerId, string jobName, int seconds, string errorType, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start tracking the external print on printer {PrinterId}.")]
    private static partial void LogStartFailed(ILogger logger, Guid printerId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {PrinterId} is printing '{JobName}', but its file could not be parsed; its filament is left for the operator.")]
    private static partial void LogFileUnreadable(ILogger logger, Guid printerId, string jobName, Exception exception);
}
