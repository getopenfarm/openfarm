using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>FR-006 — decrements RemainingWeightGrams from recorded consumption; when it reaches exactly zero, defers to <see cref="IAutoContinuationResolver"/> (FR-009).</summary>
public sealed class RecordSpoolUsageCommandHandler(
    ISpoolRepository spoolRepository,
    ISpoolUsageRecordRepository usageRecordRepository,
    IAutoContinuationResolver autoContinuationResolver) : ICommandHandler<RecordSpoolUsageCommand, RecordSpoolUsageResult>
{
    public async Task<RecordSpoolUsageResult> HandleAsync(RecordSpoolUsageCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        spool.RecordUsage(command.GramsConsumed);

        var record = SpoolUsageRecord.Create(spool.Id, command.GramsConsumed, command.JobId, SpoolUsageSource.Manual);
        await usageRecordRepository.AddAsync(record, cancellationToken);

        await spoolRepository.SaveChangesAsync(cancellationToken);

        var autoContinuation = spool.RemainingWeightGrams == 0
            ? await autoContinuationResolver.ResolveAsync(spool, cancellationToken)
            : AutoContinuationResult.None;

        return new RecordSpoolUsageResult(spool, autoContinuation);
    }
}
