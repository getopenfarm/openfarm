using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Production;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// The single writer of every telemetry-observable <see cref="PrintJob"/> transition for a
/// live-integrated printer (research.md R12) — a command handler never calls a <c>PrintJob.Mark*</c>
/// method itself for one of these; only this, driven off the printer's own reported <c>gcodeState</c>,
/// does. Looks up whichever job currently has <c>PrinterId</c> equal to the reporting
/// printer and a non-terminal status, and drives at most one transition per event — a repeated
/// gcodeState that already matches the job's current status is a no-op by construction, since every
/// branch below only fires from the specific "before" status it expects.
/// </summary>
public sealed class JobTelemetrySync(IServiceProvider services) : IJobTelemetrySync
{
    public async Task ObserveAsync(Guid printerId, string gcodeState, DateTimeOffset? unseenSinceUtc, CancellationToken cancellationToken)
    {
        var state = gcodeState.Trim().ToUpperInvariant();
        if (state is "FINISH" or "FAILED" or "IDLE") ExternalPrintSync.ClearAttempt(printerId);

        var jobRepository = services.GetRequiredService<IPrintJobRepository>();
        // Not an OpenFarm job: a print started at the printer itself is tracked the same way.
        Print? print = await jobRepository.GetActiveJobByDispatchedPrinterIdAsync(printerId, cancellationToken);
        if (print is null && services.GetService<IExternalPrintRepository>() is { } externalPrints)
            print = await externalPrints.GetOpenByPrinterIdAsync(printerId, cancellationToken);
        if (print is null) return;
        var cancelRequested = print is PrintJob { CancelRequestedAtUtc: not null };

        switch (state)
        {
            case "INIT" or "PREPARE" or "SLICING" or "RUNNING":
                if (print is PrintJob { Status: PrintJobStatus.Queued })
                {
                    print.MarkStarted();
                    // The printer still holds the previous print's progress; a job that fails before reporting
                    // any layer must not inherit it.
                    if (await services.GetRequiredService<IPrinterRepository>().GetByIdAsync(printerId, cancellationToken) is { } starting)
                        starting.ResetPrintProgress();
                    await SaveAndPublishAsync(jobRepository, print, cancellationToken);
                }
                else if (print.Status == PrintJobStatus.Paused)
                {
                    print.MarkResumed();
                    await SaveAndPublishAsync(jobRepository, print, cancellationToken);
                }

                break;

            case "PAUSE":
                if (print.Status == PrintJobStatus.Printing)
                {
                    print.MarkPaused();
                    await SaveAndPublishAsync(jobRepository, print, cancellationToken);
                }

                break;

            case "FINISH":
                if (print.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
                {
                    var outcome = cancelRequested ? PrintJobStatus.Cancelled : PrintJobStatus.Completed;
                    await CloseAsync(jobRepository, print, outcome, printerId, unseenSinceUtc, cancellationToken);
                }

                break;

            case "FAILED":
                if (print.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
                    await CloseAsync(jobRepository, print, ResolveTerminalStatus(cancelRequested), printerId, unseenSinceUtc, cancellationToken);

                break;

            case "IDLE":
                // Back to idle with this print still open: the printer rebooted or lost power, or the print ended while nobody
                // watched. The last progress seen decides, as the U1 connector does within one connection. The plate may hold
                // the print either way, so the printer waits for Mark ready, whoever started the print.
                if (print.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
                {
                    var printer = await services.GetRequiredService<IPrinterRepository>().GetByIdAsync(printerId, cancellationToken);
                    var outcome = cancelRequested ? PrintJobStatus.Cancelled
                        : PrintEnd.LooksFinished(printer?.PrintProgressPercent, printer?.CurrentLayer, printer?.TotalLayers) ? PrintJobStatus.Completed
                        : PrintJobStatus.Failed;
                    printer?.ObserveStatus("FINISH", printer.StateObservedAtUtc);
                    await CloseAsync(jobRepository, print, outcome, printerId, unseenSinceUtc, cancellationToken);
                }

                break;
        }
    }

    /// <summary>
    /// Ends the print with <paramref name="outcome"/>, deducts its filament and saves. Seen only after a silence, it ended back
    /// in that silence, not when OpenFarm heard of it.
    /// </summary>
    private async Task CloseAsync(
        IPrintJobRepository jobRepository, Print print, PrintJobStatus outcome, Guid printerId, DateTimeOffset? unseenSinceUtc,
        CancellationToken cancellationToken)
    {
        var printer = await services.GetRequiredService<IPrinterRepository>().GetByIdAsync(printerId, cancellationToken);
        DateTimeOffset? endedAtUtc = unseenSinceUtc is { } lastHeard
            ? PrintEnd.Infer(outcome, lastHeard, printer?.RemainingTimeMinutes, DateTimeOffset.UtcNow)
            : null;
        if (outcome == PrintJobStatus.Completed && unseenSinceUtc is not null && print is PrintJob { ObjectExecution: { } interrupted } interruptedJob)
            interruptedJob.RecordObjectExecution(interrupted with { Observed = false, RequestError = "Confirm the finished units after the interrupted printer connection." });
        switch (outcome)
        {
            case PrintJobStatus.Completed: print.MarkCompleted(endedAtUtc); break;
            case PrintJobStatus.Cancelled: print.MarkCancelled(endedAtUtc); break;
            default: print.MarkFailed(reason: "The printer reported a failed print.", endedAtUtc); break;
        }

        if (print is PrintJob { ObjectExecution: { ArtifactPath: not null } execution } objectJob)
        {
            try
            {
                var storage = services.GetRequiredService<OpenFarm.Application.Parts.IPartFileStorage>();
                await using var retained = await storage.OpenReadAsync(execution.ArtifactPath, cancellationToken);
                using var buffer = new MemoryStream();
                await retained.CopyToAsync(buffer, cancellationToken);
                if (ObjectExecutionArtifact.Hash(buffer.ToArray()) != execution.ArtifactHash)
                    throw new IOException("Retained execution artifact checksum changed.");
                var usage = JobFilamentUsageReader.ReadForExecution(buffer.ToArray(), execution.Manifest.Plate, execution);
                var lines = JobFilamentUsageMatcher.Match(objectJob.Filaments.Select(f => (f.MaterialType, f.DetectedColorHex)).ToArray(), usage.Usages);
                objectJob.BindFilamentUsage(objectJob.Filaments.OrderBy(f => f.ToolIndex).Select((f, i) => (f.SpoolId, lines[i].Grams,
                    FilamentUsageCurve.FromExtrusion(usage.Extrusion.Marks, lines[i].Tools.Where(usage.Extrusion.Tools.ContainsKey).Select(t => usage.Extrusion.Tools[t])))).ToArray());
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
            {
                // Retain the last known estimate; an unavailable artifact must never wedge terminal telemetry.
                await services.GetRequiredService<IActivityLogWriter>().WriteAsync(ActivityLogEntry.Create(
                    "PrintJob", objectJob.Id.ToString(), "ObjectFilamentEstimateUnavailable", null, ActorType.System), cancellationToken);
            }
        }
        await DeductFilamentAsync(print, printerId, cancellationToken);
        await SaveAndPublishAsync(jobRepository, print, cancellationToken);

        if (outcome != PrintJobStatus.Failed && print is PrintJob job)
        {
            var completionService = services.GetRequiredService<ProductionRunCompletionService>();
            await completionService.TryCompleteRunForRequirementAsync(job.PartRequirementId, cancellationToken);
        }
    }

    /// <summary>
    /// FR-024c's disambiguation, isolated as a pure function for direct unit testing: OpenFarm's own
    /// stop intent always wins regardless of which terminal gcodeState triggered this call; absent
    /// that intent (a stop performed directly at the printer), the documented fallback is
    /// <see cref="PrintJobStatus.Failed"/> — an unexplained terminal state is never speculatively
    /// reclassified as <see cref="PrintJobStatus.Cancelled"/>.
    /// </summary>
    internal static PrintJobStatus ResolveTerminalStatus(bool cancelRequested) =>
        cancelRequested ? PrintJobStatus.Cancelled : PrintJobStatus.Failed;

    /// <summary>
    /// Runs before the printer's own progress is overwritten by this event (HandleEventAsync records telemetry after
    /// this sync), so it reads the last progress observed while printing.
    /// </summary>
    private async Task DeductFilamentAsync(Print print, Guid printerId, CancellationToken cancellationToken)
    {
        var printer = await services.GetRequiredService<IPrinterRepository>().GetByIdAsync(printerId, cancellationToken);
        // Kept on the print so an operator can finish a skipped deduction later, long after the printer moved on.
        print.RecordStopProgress(printer?.CurrentLayer, printer?.TotalLayers, printer?.PrintProgressPercent, printer?.ActiveAlerts);
        if (print is ExternalPrint { Filaments.Count: 0 } unread)
        {
            // Its file never arrived: one line of unknown grams, which deduction skips, so the operator sees what wasn't taken.
            unread.AttachFilaments(null);
            services.GetRequiredService<IExternalPrintRepository>().TrackNewFilaments(unread.Filaments);
        }

        if (print is ExternalPrint external && printer is not null) await BindToolSpoolsAsync(external, printer, cancellationToken);
        var progress = new JobProgress(print.Status, print.StopLayer, print.StopTotalLayers, print.StopPercent);
        await services.GetRequiredService<JobFilamentAutoDeduction>().ApplyAsync(print, progress, cancellationToken);
    }

    /// <summary>
    /// Which tray fed which tool of a multi-colour print isn't reported, and a print joined mid-print misses early trays, so the
    /// tools are paired with the AMS trays by material and colour now, from the last AMS state seen while printing. Spools are
    /// resolved here, not at the start: they may have been loaded in OpenFarm after the print began.
    /// </summary>
    private async Task BindToolSpoolsAsync(ExternalPrint print, Printer printer, CancellationToken cancellationToken)
    {
        var tools = print.Filaments.Where(f => f.SpoolId is null).Select(f => (f.ToolIndex, f.MaterialType, f.DetectedColorHex)).ToList();
        if (tools.Count == 0) return;
        var trays = printer.FeedReadings.Where(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit)
            .SelectMany(u => u.Slots.Where(s => !s.Empty)
                .Select(s => new LoadedTray<FeedSlotRef>(new FeedSlotRef(u.Kind, u.UnitIndex, s.SlotPosition), s.MaterialType, s.ColorHex)))
            .ToList();
        var resolver = services.GetRequiredService<FeedTrayResolver>();
        var spools = new Dictionary<int, Guid>();
        foreach (var (tool, slot) in ToolTrayMatcher.Match(tools, trays, print.ObservedSlots))
            if (await resolver.ResolveSpoolAsync(printer.Id, slot, cancellationToken) is { } spool) spools[tool] = spool;
        print.BindTools(spools);
    }

    private async Task SaveAndPublishAsync(IPrintJobRepository jobRepository, Print print, CancellationToken cancellationToken)
    {
        // A print started at the printer already drives the printer's own status; only OpenFarm's jobs mirror it back.
        if (print is PrintJob job)
        {
            var printers = services.GetRequiredService<IPrinterRepository>();
            if (job.PrinterId is { } printerId && await printers.GetByIdAsync(printerId, cancellationToken) is { } printer)
            {
                printer.ObserveStatus(job.IsClosed ? "FINISH" : "RUNNING");
            }

            await jobRepository.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await services.GetRequiredService<IExternalPrintRepository>().SaveChangesAsync(cancellationToken);
        }

        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();
        if (print is PrintJob || print.IsClosed)
        {
            var entry = ActivityLogEntry.Create(
                entityType: print is PrintJob ? "PrintJob" : "ExternalPrint", entityId: print.Id.ToString(),
                eventType: print is PrintJob ? "PrintJobStatusObserved" : "ExternalPrintFinished", actorId: null, actorType: ActorType.System);
            await activityLogWriter.WriteAsync(entry, cancellationToken);
        }

        var publisher = services.GetRequiredService<IProductionEventPublisher>();
        await publisher.PrintJobStatusChangedAsync(print.Id, print.Status, cancellationToken);
        if (print.IsClosed) await publisher.NotificationsChangedAsync(cancellationToken);
    }
}
