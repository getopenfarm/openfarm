using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>How far the print got when it ended: the outcome, and what the printer last reported.</summary>
public sealed record JobProgress(PrintJobStatus Outcome, int? CurrentLayer = null, int? TotalLayers = null, int? Percent = null);

/// <summary>
/// Deducts a job's filament from the spools bound to its lines at dispatch (FR-013/FR-013a). Runs on the
/// terminal transition; the caller saves, so transition and deduction commit together. Deducts only the
/// difference from what a line already recorded, so a repeated terminal event never double-deducts.
/// A line without a bound spool or a known estimate is skipped and logged — never thrown — so telemetry
/// handling can't be wedged by one bad spool; the operator adjusts through manual spool usage.
/// </summary>
public sealed class JobFilamentAutoDeduction(
    ISpoolRepository spoolRepository,
    ISpoolUsageRecordRepository usageRecordRepository,
    IAutoContinuationResolver continuation,
    IActivityLogWriter activityLogWriter)
{
    /// <summary>
    /// Fraction of one line's filament consumed: all of it for a finished print. Otherwise what that colour had used by the
    /// layer the printer stopped on, from the line's usage curve; without a curve, an even spread over the layers (or the
    /// reported percent), which is right only when every colour is used evenly.
    /// </summary>
    public static decimal? Fraction(JobFilamentConsumption line, JobProgress progress) =>
        progress.Outcome == PrintJobStatus.Completed ? 1m
        : line.UsageCurve is { } curve && progress.CurrentLayer is { } layer ? (decimal)curve.ShareAt(layer)
        : progress.CurrentLayer is { } evenLayer && progress.TotalLayers is > 0 ? Math.Clamp((decimal)evenLayer / progress.TotalLayers.Value, 0m, 1m)
        : progress.Percent is { } percent ? Math.Clamp(percent / 100m, 0m, 1m)
        : null;

    public async Task ApplyAsync(PrintJob job, JobProgress progress, CancellationToken cancellationToken)
    {
        var skipped = false;
        var depleted = new List<Spool>();
        foreach (var line in job.FilamentConsumption)
        {
            if (Fraction(line, progress) is not { } fraction || line.SpoolId is not { } spoolId || line.EstimatedGrams is not { } estimate)
            {
                skipped = true;
                continue;
            }

            var target = decimal.Round(estimate * fraction, 2);
            var delta = target - (line.GramsConsumed ?? 0);
            if (delta <= 0) continue;

            var spool = await spoolRepository.GetByIdAsync(spoolId, cancellationToken);
            if (spool is null || spool.State != SpoolState.Using)
            {
                skipped = true;
                continue;
            }

            spool.RecordUsage(delta);
            job.RecordFilamentConsumption(line.Id, spoolId, target);
            await usageRecordRepository.AddAsync(
                SpoolUsageRecord.Create(job.OrganizationId, spoolId, delta, job.Id, SpoolUsageSource.Job), cancellationToken);
            if (spool.RemainingWeightGrams == 0) depleted.Add(spool);
        }

        foreach (var spool in depleted) await continuation.ResolveAsync(spool, cancellationToken);

        if (skipped)
        {
            await activityLogWriter.WriteAsync(ActivityLogEntry.Create(
                job.OrganizationId, "PrintJob", job.Id.ToString(), "PrintJobFilamentDeductionSkipped", null, ActorType.System), cancellationToken);
        }
    }
}
