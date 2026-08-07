using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-016a's completion check, shared by every job-completion path (T060's manual-brand handler and
/// <c>JobTelemetrySync</c>'s live-integrated one) so the rule lives in exactly one place. A run
/// reaches <see cref="ProductionRunStatus.Complete"/> the moment every finite requirement across
/// every line is satisfied — this is always re-checked from scratch after a job completes, never
/// incrementally tracked, since the codebase's stated scale (SC-005) makes that cheap. FR-016b's
/// named-run confirmation is a frontend-only concern layered on top of this same, single trigger
/// (data-model.md: "never a second server-side flow") — nothing here treats a named run differently.
/// </summary>
public sealed class ProductionRunCompletionService(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IProductionEventPublisher publisher)
{
    /// <summary>
    /// Re-evaluates the run owning <paramref name="partRequirementId"/> and marks it Complete if every
    /// finite requirement is now satisfied. A no-op if the run is missing, not Active, or still short.
    /// </summary>
    public async Task TryCompleteRunForRequirementAsync(Guid partRequirementId, CancellationToken cancellationToken)
    {
        var requirement = await requirementRepository.GetByIdAsync(partRequirementId, cancellationToken);
        if (requirement is null)
        {
            return;
        }

        var run = await runRepository.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
        if (run is null || run.Status != ProductionRunStatus.Active)
        {
            return;
        }

        if (!await AllFiniteRequirementsSatisfiedAsync(run, cancellationToken))
        {
            return;
        }

        run.MarkComplete(allFiniteRequirementsSatisfied: true);
        await runRepository.SaveChangesAsync(cancellationToken);
        await publisher.ProductionRunStatusChangedAsync(run.OrganizationId, run.Id, run.Status, cancellationToken);
    }

    private async Task<bool> AllFiniteRequirementsSatisfiedAsync(ProductionRun run, CancellationToken cancellationToken)
    {
        foreach (var line in run.Lines)
        {
            foreach (var requirement in await requirementRepository.ListByRunLineIdAsync(line.Id, cancellationToken))
            {
                if (requirement.Quantity is null)
                {
                    // Unlimited line — never blocks completion (FR-016a).
                    continue;
                }

                var jobs = await jobRepository.ListByPartRequirementIdAsync(requirement.Id, cancellationToken);

                // A not-yet-requeued shortfall is, by definition, never satisfied (FR-025, research.md R13).
                if (jobs.Any(j => j.Status is PrintJobStatus.Cancelled or PrintJobStatus.Failed && j.RequeuedAsJobId is null))
                {
                    return false;
                }

                var completedOutput = jobs.Where(j => j.Status == PrintJobStatus.Completed).Sum(j => j.PlateQuantity);
                if (completedOutput < requirement.Quantity!.Value)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
