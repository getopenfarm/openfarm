using OpenFarm.Domain.Production;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-016a's completion check, shared by every path that can finish a run's last plate (the manual-brand handler,
/// <c>JobTelemetrySync</c>'s live one, and a write-off) so the rule lives in one place. A run with finite lines reaches
/// <see cref="ProductionRunStatus.Complete"/> the moment every finite requirement is satisfied
/// (<see cref="PartRequirement.IsSatisfiedBy"/>), re-checked from scratch each time — cheap at SC-005's scale. Named or
/// not, it completes on its own; Close is the operator's sign-off.
/// </summary>
public sealed class ProductionRunCompletionService(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IProductionEventPublisher publisher,
    IActivityLogWriter? activityLogWriter = null)
{
    /// <summary>Completes the run owning <paramref name="partRequirementId"/> if that was its last finite work; otherwise a no-op.</summary>
    public async Task TryCompleteRunForRequirementAsync(Guid partRequirementId, CancellationToken cancellationToken)
    {
        var requirement = await requirementRepository.GetByIdAsync(partRequirementId, cancellationToken);
        if (requirement is null)
        {
            return;
        }

        var run = await runRepository.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
        if (run is null || run.Status != ProductionRunStatus.Active || !run.Lines.Any(l => l.Quantity is not null)
            || await FiniteWorkRemainsAsync(run, cancellationToken))
        {
            return;
        }

        run.MarkComplete();
        await runRepository.SaveChangesAsync(cancellationToken);
        if (activityLogWriter is not null)
            await activityLogWriter.WriteAsync(ActivityLogEntry.Create("ProductionRun", run.Id.ToString(), "ProductionRunCompleted", null, ActorType.System), cancellationToken);
        await publisher.ProductionRunStatusChangedAsync(run.Id, run.Status, cancellationToken);
    }

    /// <summary>Whether any finite requirement is still short; false for a run of unlimited lines only.</summary>
    public async Task<bool> FiniteWorkRemainsAsync(ProductionRun run, CancellationToken cancellationToken)
    {
        foreach (var line in run.Lines.Where(l => l.Quantity is not null))
        {
            foreach (var requirement in await requirementRepository.ListByRunLineIdAsync(line.Id, cancellationToken))
            {
                if (!requirement.IsSatisfiedBy(await jobRepository.ListByPartRequirementIdAsync(requirement.Id, cancellationToken)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
