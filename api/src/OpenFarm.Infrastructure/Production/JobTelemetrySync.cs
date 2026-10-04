using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Production;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// The single writer of every telemetry-observable <see cref="PrintJob"/> transition for a
/// live-integrated printer (research.md R12) — a command handler never calls a <c>PrintJob.Mark*</c>
/// method itself for one of these; only this, driven off the printer's own reported <c>gcodeState</c>,
/// does. Looks up whichever job currently has <c>DispatchedPrinterId</c> equal to the reporting
/// printer and a non-terminal status, and drives at most one transition per event — a repeated
/// gcodeState that already matches the job's current status is a no-op by construction, since every
/// branch below only fires from the specific "before" status it expects.
/// </summary>
public sealed class JobTelemetrySync(IServiceProvider services) : IJobTelemetrySync
{
    public async Task ObserveAsync(Guid printerId, string gcodeState, CancellationToken cancellationToken)
    {
        var jobRepository = services.GetRequiredService<IPrintJobRepository>();
        var job = await jobRepository.GetActiveJobByDispatchedPrinterIdAsync(printerId, cancellationToken);
        if (job is null)
        {
            return;
        }

        switch (gcodeState.Trim().ToUpperInvariant())
        {
            case "INIT" or "PREPARE" or "SLICING" or "RUNNING":
                if (job.Status == PrintJobStatus.Queued)
                {
                    job.MarkStarted();
                    await SaveAndPublishAsync(jobRepository, job, cancellationToken);
                }
                else if (job.Status == PrintJobStatus.Paused)
                {
                    job.MarkResumed();
                    await SaveAndPublishAsync(jobRepository, job, cancellationToken);
                }

                break;

            case "PAUSE":
                if (job.Status == PrintJobStatus.Printing)
                {
                    job.MarkPaused();
                    await SaveAndPublishAsync(jobRepository, job, cancellationToken);
                }

                break;

            case "FINISH":
                if (job.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
                {
                    if (job.CancelRequestedAtUtc is not null) job.MarkCancelled();
                    else job.MarkCompleted();
                    await SaveAndPublishAsync(jobRepository, job, cancellationToken);

                    var completionService = services.GetRequiredService<ProductionRunCompletionService>();
                    await completionService.TryCompleteRunForRequirementAsync(job.PartRequirementId, cancellationToken);
                }

                break;

            case "FAILED":
                if (job.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
                {
                    if (ResolveTerminalStatus(job.CancelRequestedAtUtc is not null) == PrintJobStatus.Cancelled)
                    {
                        job.MarkCancelled();
                    }
                    else
                    {
                        job.MarkFailed(reason: "The printer reported a failed print.");
                    }

                    await SaveAndPublishAsync(jobRepository, job, cancellationToken);
                }

                break;
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

    private async Task SaveAndPublishAsync(IPrintJobRepository jobRepository, PrintJob job, CancellationToken cancellationToken)
    {
        var printers = services.GetRequiredService<IPrinterRepository>();
        if (job.DispatchedPrinterId is { } printerId && await printers.GetByIdAsync(printerId, cancellationToken) is { } printer)
        {
            printer.ObserveStatus(job.Status is PrintJobStatus.Completed or PrintJobStatus.Failed or PrintJobStatus.Cancelled ? "FINISH" : "RUNNING");
        }
        await jobRepository.SaveChangesAsync(cancellationToken);

        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();
        var entry = ActivityLogEntry.Create(
            job.OrganizationId, entityType: "PrintJob", entityId: job.Id.ToString(),
            eventType: "PrintJobStatusObserved", actorId: null, actorType: ActorType.System);
        await activityLogWriter.WriteAsync(entry, cancellationToken);

        var publisher = services.GetRequiredService<IProductionEventPublisher>();
        await publisher.PrintJobStatusChangedAsync(job.OrganizationId, job.Id, job.Status, cancellationToken);
    }
}
