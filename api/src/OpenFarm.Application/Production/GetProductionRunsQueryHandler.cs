using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class GetProductionRunsQueryHandler(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    ProductionRunDisplayLabelResolver labelResolver)
    : IQueryHandler<GetProductionRunsQuery, ProductionRunsPage>
{
    public async Task<ProductionRunsPage> HandleAsync(GetProductionRunsQuery query, CancellationToken cancellationToken)
    {
        var (runs, totalCount, statusCounts) = await runRepository.ListAsync(
            query.Page, query.PageSize, cancellationToken, query.Sorting, query.Status, query.Search);

        // ponytail: two queries per run on the page (at most 200 rows); batch by run ids if the list ever feels slow.
        var items = new List<ProductionRunSummary>();
        foreach (var run in runs)
        {
            var requirements = await requirementRepository.ListByProductionRunIdAsync(run.Id, cancellationToken);
            var jobsByRequirement = (await jobRepository.ListByProductionRunIdAsync(run.Id, cancellationToken))
                .ToLookup(j => j.PartRequirementId);

            decimal targetUnits = 0, completedUnits = 0;
            int writtenOffUnits = 0, producedUnits = 0;
            var plates = PlateCounts.None;
            foreach (var requirement in requirements)
            {
                var jobs = jobsByRequirement[requirement.Id].ToList();
                var completed = jobs.Sum(j => j.ProducedUnits);
                if (requirement.Quantity is not { } target)
                {
                    producedUnits += completed;
                    continue;
                }

                targetUnits += target;
                completedUnits += Math.Min(completed, target);
                writtenOffUnits += jobs.Sum(j => j.WrittenOffUnits);
                plates = plates.Add(PlateCounts.Of(jobs));
            }

            var blockedCount = jobsByRequirement.SelectMany(g => g)
                .Count(j => j.Status == PrintJobStatus.Queued && j.LastBlockReason is not null);
            items.Add(new ProductionRunSummary(
                run.Id, await labelResolver.ResolveAsync(run, cancellationToken), run.Name, run.Status, run.DueDate, run.CreatedAtUtc,
                run.Lines.Select(l => new RunLineBrief(l.TargetName ?? "Unknown", l.Quantity)).ToList(),
                targetUnits, completedUnits, writtenOffUnits, producedUnits, plates, blockedCount));
        }

        return new ProductionRunsPage(items, totalCount, statusCounts);
    }
}
