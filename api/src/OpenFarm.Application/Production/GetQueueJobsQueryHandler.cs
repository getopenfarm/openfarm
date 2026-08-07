using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.Production;

public sealed class GetQueueJobsQueryHandler(
    IPrintJobRepository jobRepository,
    IPartRequirementRepository requirementRepository,
    IPartRepository partRepository,
    IProductionRunRepository runRepository,
    ProductionRunDisplayLabelResolver labelResolver) : IQueryHandler<GetQueueJobsQuery, QueueJobsPage>
{
    public async Task<QueueJobsPage> HandleAsync(GetQueueJobsQuery query, CancellationToken cancellationToken)
    {
        var (jobs, totalCount) = await jobRepository.ListQueueAsync(query.BackfillOnly, query.Page, query.PageSize, cancellationToken);

        var runLabelCache = new Dictionary<Guid, (Guid RunId, string DisplayLabel)>();
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
                var displayLabel = run is null ? "Unknown run" : await labelResolver.ResolveAsync(run, cancellationToken);
                runInfo = (run?.Id ?? Guid.Empty, displayLabel);
                runLabelCache[requirement.RunLineId] = runInfo;
            }

            items.Add(new QueueJobRow(
                job.Id, job.QueuePosition, job.Status, job.AssignmentMode, job.PinnedPrinterId, job.DispatchedPrinterId,
                IsBlocked: false, requirement.PartId, part?.Name ?? "Unknown part", job.PlateQuantity,
                requirement.RunLineId, runInfo.RunId, runInfo.DisplayLabel));
        }

        return new QueueJobsPage(items, totalCount);
    }
}
