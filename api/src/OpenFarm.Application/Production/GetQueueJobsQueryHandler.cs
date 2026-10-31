using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class GetQueueJobsQueryHandler(
    IPrintJobRepository jobRepository,
    IPartRequirementRepository requirementRepository,
    IPartRepository partRepository,
    IProductionRunRepository runRepository,
    ProductionRunDisplayLabelResolver labelResolver,
    IPrintJobEligibilityQuery eligibilityQuery,
    IExternalPrintRepository? externalPrintRepository = null, IProductionDemandRepository? demands = null) : IQueryHandler<GetQueueJobsQuery, QueueJobsPage>
{
    public async Task<QueueJobsPage> HandleAsync(GetQueueJobsQuery query, CancellationToken cancellationToken)
    {
        if (query.ExternalOnly) return await ListExternalAsync(query, cancellationToken);

        var (jobs, totalCount, statusCounts) = await jobRepository.ListQueueAsync(query.BackfillOnly, query.Page, query.PageSize, cancellationToken, query.Status, query.Search);

        var eligibleIds = await eligibilityQuery.GetEligibleJobIdsAsync(jobs, cancellationToken);
        var runLabelCache = new Dictionary<Guid, (Guid? RunId, string DisplayLabel, bool IsOpen)>();
        var items = new List<QueueJobRow>();

        foreach (var job in jobs)
        {
            var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken);
            if (requirement is null)
            {
                continue;
            }

            var part = await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);

            if (!runLabelCache.TryGetValue(requirement.RunLineId, out var runInfo))
            {
                var run = await runRepository.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
                var displayLabel = run is null ? "" : await labelResolver.ResolveAsync(run, cancellationToken);
                runInfo = (run?.Id, displayLabel, run?.IsOpen ?? true);
                runLabelCache[requirement.RunLineId] = runInfo;
            }

            // FR-007c and the rest of matching: a queued, unsent job no printer can take now.
            var isBlocked = job.Status == PrintJobStatus.Queued && job.DispatchedAtUtc is null && !eligibleIds.Contains(job.Id);

            items.Add(new QueueJobRow(
                job.Id, job.QueuePosition, job.Status, job.AssignmentMode, job.PinnedPrinterId, job.PrinterId,
                isBlocked, requirement.PartId, job.GetProductionPart()?.Name ?? requirement.PartName ?? part?.Name ?? "Unknown part", job.PlateQuantity,
                requirement.RunLineId, runInfo.RunId, runInfo.DisplayLabel,
                isBlocked ? await eligibilityQuery.GetBlockingReasonAsync(job, cancellationToken) : null, part is not null, job.GetSnapshotState(), job.SnapshotRecoveryReason,
                job.Source, job.StartedAtUtc, job.EndedAtUtc, job.Status == PrintJobStatus.Queued ? job.LastBlockReason : null, demands is null ? null : (await demands.GetAsync(requirement.RunLineId, cancellationToken))?.Mode.ToString(), requirement.Quantity is not null && job.IsUnresolvedLoss && job.ProductionSnapshotJson is not null && runInfo.IsOpen && !(job.Status == PrintJobStatus.Completed && job.ObjectExecution is { Observed: false }), job.EffectiveTargetUnits, job.ProducedUnits, job.MissingUnits));
        }

        return new QueueJobsPage(items, totalCount, statusCounts);
    }

    /// <summary>Prints the printer started itself: newest first, no queue, run or part to resolve.</summary>
    private async Task<QueueJobsPage> ListExternalAsync(GetQueueJobsQuery query, CancellationToken cancellationToken)
    {
        if (externalPrintRepository is null) return new QueueJobsPage([], 0, new Dictionary<string, int>());
        var (prints, totalCount, statusCounts) = await externalPrintRepository.ListAsync(query.Page, query.PageSize, query.Status, query.Search, cancellationToken);
        var items = prints.Select(print => new QueueJobRow(
            print.Id, null, print.Status, AssignmentMode.Unassigned, null, print.PrinterId, false, null, print.Name, 1,
            null, null, "Printer-started", null, false, "Ready", null, print.Source, print.StartedAtUtc, print.EndedAtUtc)).ToList();
        return new QueueJobsPage(items, totalCount, statusCounts);
    }
}
