using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class GetProductionRunsQueryHandler(
    IProductionRunRepository runRepository, IPrintJobRepository jobRepository, ProductionRunDisplayLabelResolver labelResolver)
    : IQueryHandler<GetProductionRunsQuery, ProductionRunsPage>
{
    public async Task<ProductionRunsPage> HandleAsync(GetProductionRunsQuery query, CancellationToken cancellationToken)
    {
        var (runs, totalCount) = await runRepository.ListAsync(query.Page, query.PageSize, cancellationToken);

        var items = new List<ProductionRunSummary>();
        foreach (var run in runs)
        {
            var jobs = await jobRepository.ListByProductionRunIdAsync(run.Id, cancellationToken);
            var jobSummary = new JobStatusSummary(
                jobs.Count(j => j.Status == PrintJobStatus.Queued),
                jobs.Count(j => j.Status == PrintJobStatus.Printing),
                jobs.Count(j => j.Status == PrintJobStatus.Paused),
                jobs.Count(j => j.Status == PrintJobStatus.Completed),
                jobs.Count(j => j.Status == PrintJobStatus.Failed),
                jobs.Count(j => j.Status == PrintJobStatus.Cancelled));

            var displayLabel = await labelResolver.ResolveAsync(run, cancellationToken);
            items.Add(new ProductionRunSummary(run.Id, displayLabel, run.Status, run.DueDate, run.Lines.Count, jobSummary));
        }

        return new ProductionRunsPage(items, totalCount);
    }
}
