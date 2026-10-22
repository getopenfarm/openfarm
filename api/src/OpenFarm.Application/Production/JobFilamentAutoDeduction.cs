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
/// The first time a line is deducted it is also costed: the price per kg of its spool, else its colour, else its filament, is
/// written onto the line and kept (<see cref="FilamentPricing"/>).
/// </summary>
public sealed class JobFilamentAutoDeduction(
    ISpoolRepository spoolRepository,
    ISpoolUsageRecordRepository usageRecordRepository,
    IAutoContinuationResolver continuation,
    IActivityLogWriter activityLogWriter,
    IFilamentRepository filamentRepository)
{
    /// <summary>
    /// Fraction of one line's filament consumed: all of it for a finished print. Otherwise what that colour had used by the
    /// layer the printer stopped on, from the line's usage curve; without a curve, an even spread over the layers (or the
    /// reported percent), which is right only when every colour is used evenly.
    /// </summary>
    public static decimal? Fraction(IFilamentUsageLine line, JobProgress progress) =>
        progress.Outcome == PrintJobStatus.Completed ? 1m
        : line.UsageCurve is { } curve && progress.CurrentLayer is { } layer ? (decimal)curve.ShareAt(layer, progress.Percent)
        : progress.CurrentLayer is { } evenLayer && progress.TotalLayers is > 0 ? Math.Clamp((decimal)evenLayer / progress.TotalLayers.Value, 0m, 1m)
        : progress.Percent is { } percent ? Math.Clamp(percent / 100m, 0m, 1m)
        : null;

    /// <summary>
    /// Deducts for a dispatched job or a print the printer started itself; log names keep the two kinds apart. A re-run by the
    /// operator passes <paramref name="logSkipped"/> false so it does not log the same skip again.
    /// </summary>
    public Task ApplyAsync(Print print, JobProgress progress, CancellationToken cancellationToken, bool logSkipped = true) =>
        print.Source == PrintSource.External
            ? DeductAsync(print, "ExternalPrint", "ExternalPrintFilamentDeductionSkipped", progress, logSkipped, cancellationToken)
            : DeductAsync(print, "PrintJob", "PrintJobFilamentDeductionSkipped", progress, logSkipped, cancellationToken);

    private async Task DeductAsync(
        Print print, string entityType, string skippedEvent, JobProgress progress, bool logSkipped, CancellationToken cancellationToken)
    {
        var ownerId = print.Id;
        var skipped = false;
        var depleted = new List<Spool>();
        foreach (var line in print.Filaments)
        {
            // Reasons a spool choice cannot fix come first, so the operator is only offered an action that can work.
            var fraction = Fraction(line, progress);
            var reason = line.EstimatedGrams is null ? FilamentSkipReason.NoEstimate
                : fraction is null ? FilamentSkipReason.NoProgress
                : line.SpoolId is null ? FilamentSkipReason.NoSpool
                : (FilamentSkipReason?)null;
            if (reason is { } missing)
            {
                print.MarkFilamentSkipped(line.Id, missing);
                skipped = true;
                continue;
            }

            var spoolId = line.SpoolId!.Value;
            var target = decimal.Round(line.EstimatedGrams!.Value * fraction!.Value, 2);
            var delta = target - (line.GramsConsumed ?? 0);
            if (delta <= 0)
            {
                // A line the operator just bound that really used nothing is settled, not skipped.
                if (line.SkippedReason is not null) print.RecordFilamentConsumption(line.Id, spoolId, target);
                continue;
            }

            var spool = await spoolRepository.GetByIdAsync(spoolId, cancellationToken);
            if (spool is null || spool.State != SpoolState.Using)
            {
                print.MarkFilamentSkipped(line.Id, FilamentSkipReason.SpoolNotLoaded);
                skipped = true;
                continue;
            }

            spool.RecordUsage(delta);
            print.RecordFilamentConsumption(line.Id, spoolId, target);
            // Costed at the price known now. The save inside AddAsync below carries it with the deduction.
            if (line.PricePerKg is null)
            {
                print.SnapshotFilamentPrice(line.Id, await ResolvePriceAsync(spool, cancellationToken));
            }

            await usageRecordRepository.AddAsync(
                SpoolUsageRecord.Create(spoolId, delta, ownerId, SpoolUsageSource.Job, spool.RemainingWeightGrams), cancellationToken);
            if (spool.RemainingWeightGrams == 0) depleted.Add(spool);
        }

        foreach (var spool in depleted) await continuation.ResolveAsync(spool, cancellationToken);

        if (skipped && logSkipped)
        {
            await activityLogWriter.WriteAsync(ActivityLogEntry.Create(
                entityType, ownerId.ToString(), skippedEvent, null, ActorType.System), cancellationToken);
        }
    }

    private async Task<ResolvedFilamentPrice?> ResolvePriceAsync(Spool spool, CancellationToken cancellationToken)
    {
        var variant = await filamentRepository.FindVariantByIdAsync(spool.FilamentVariantId, cancellationToken);
        var filament = variant is null ? null : await filamentRepository.GetByIdAsync(variant.FilamentId, cancellationToken);
        return FilamentPricing.Resolve(spool, variant, filament);
    }
}
