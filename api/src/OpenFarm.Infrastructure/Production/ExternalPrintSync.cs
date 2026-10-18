using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Deducts filament for prints the printer started itself (Bambu Studio, the Handy app, the touchscreen, an SD card).
/// Once a print is seen running with no OpenFarm job behind it, the printed file is read back from the printer and its
/// per-tool grams and layer curve are frozen on an <see cref="ExternalPrint"/>; the trays the printer reports active are
/// recorded as they appear; the terminal state deducts through the same <see cref="JobFilamentAutoDeduction"/> a dispatched
/// job uses, so the accuracy is the same. Every failure here (no secret, file not found, unreadable) skips quietly: external
/// prints are best effort and must never wedge telemetry handling.
/// </summary>
public sealed partial class ExternalPrintSync(
    IExternalPrintRepository prints,
    IPrintJobRepository jobs,
    IPrinterRepository printers,
    IConnectionSecretRepository secrets,
    ISecretProtector secretProtector,
    IPrinterConnectorFactory connectorFactory,
    FeedTrayResolver trayResolver,
    JobFilamentAutoDeduction deduction,
    IActivityLogWriter activityLog,
    ILogger<ExternalPrintSync> logger)
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    // ponytail: process-local, so a restart mid-print retries the fetch once; persist an attempt marker if that ever matters.
    private static readonly ConcurrentDictionary<Guid, string> FetchAttempted = new();

    private static readonly Regex PlateName = PlateRegex();

    /// <summary>Run after the printer's telemetry is recorded: opens an external print on first sight of one running, and tracks trays.</summary>
    public async Task ObserveTelemetryAsync(Printer printer, CancellationToken cancellationToken)
    {
        if (printer.ReportedState?.ToUpperInvariant() is not ("RUNNING" or "PAUSE")) return;

        var open = await prints.GetOpenByPrinterIdAsync(printer.Id, cancellationToken);
        if (open is null)
        {
            if (string.IsNullOrWhiteSpace(printer.CurrentJobName)
                || FetchAttempted.TryGetValue(printer.Id, out var attempted) && attempted == printer.CurrentJobName
                || await jobs.GetActiveJobByDispatchedPrinterIdAsync(printer.Id, cancellationToken) is not null) return;
            FetchAttempted[printer.Id] = printer.CurrentJobName;
            open = await TryStartAsync(printer, cancellationToken);
            if (open is null) return;
        }

        if (printer.ActiveTrayId is { } tray and < 255
            && open.ObserveTray(tray, await trayResolver.ResolveSpoolAsync(printer.Id, tray, cancellationToken)))
            await prints.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Run on a reported gcode state, before the printer's last progress is overwritten: closes the open external print and deducts.</summary>
    public async Task ObserveStateAsync(Guid printerId, string gcodeState, CancellationToken cancellationToken)
    {
        var outcome = gcodeState.Trim().ToUpperInvariant() switch
        {
            "FINISH" => PrintJobStatus.Completed,
            "FAILED" => PrintJobStatus.Failed,
            _ => (PrintJobStatus?)null,
        };
        if (outcome is null) return;

        FetchAttempted.TryRemove(printerId, out _);
        if (await prints.GetOpenByPrinterIdAsync(printerId, cancellationToken) is not { } open) return;

        var printer = await printers.GetByIdAsync(printerId, cancellationToken);
        open.Finish(outcome.Value);
        await deduction.ApplyAsync(open, new JobProgress(outcome.Value, printer?.CurrentLayer, printer?.TotalLayers, printer?.PrintProgressPercent), cancellationToken);
        await prints.SaveChangesAsync(cancellationToken);
        await activityLog.WriteAsync(ActivityLogEntry.Create(
            open.OrganizationId, "ExternalPrint", open.Id.ToString(), "ExternalPrintFinished", null, ActorType.System), cancellationToken);
    }

    private async Task<ExternalPrint?> TryStartAsync(Printer printer, CancellationToken cancellationToken)
    {
        try
        {
            var secret = await secrets.GetByPrinterSerialNumberAsync(printer.SerialNumber, cancellationToken);
            if (secret is null) return null;
            var details = new PrinterConnectionDetails(
                printer.Brand, printer.ModelCode, printer.IpAddress, printer.SerialNumber, secretProtector.Unprotect(secret.EncryptedAccessCode));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FetchTimeout);
            var file = await connectorFactory.CreateConnector(details)
                .FetchPrintFileAsync(details, printer.CurrentJobName, printer.CurrentGcodeFile, timeout.Token);
            if (file is null)
            {
                LogFileNotFound(logger, printer.Id, printer.CurrentJobName!);
                return null;
            }

            var plate = PlateName.Match(printer.CurrentGcodeFile ?? "") is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
            var usage = JobFilamentUsageReader.Read(file, plate);
            if (usage.Usages.Count == 0) return null;

            var print = ExternalPrint.Start(printer.OrganizationId, printer.Id, printer.CurrentJobName!, usage.Usages.Select(u =>
                new ExternalFilamentSpec(u.ToolIndex, u.MaterialType, u.ColorHex, u.Grams,
                    usage.Extrusion.Tools.TryGetValue(u.ToolIndex, out var tool) ? FilamentUsageCurve.FromExtrusion(usage.Extrusion.Marks, [tool]) : null)));
            await prints.AddAsync(print, cancellationToken);
            await prints.SaveChangesAsync(cancellationToken);
            await activityLog.WriteAsync(ActivityLogEntry.Create(
                print.OrganizationId, "ExternalPrint", print.Id.ToString(), "ExternalPrintStarted", null, ActorType.System), cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start tracking the external print on printer {PrinterId}.")]
    private static partial void LogStartFailed(ILogger logger, Guid printerId, Exception exception);
}
