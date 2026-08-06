using OpenFarm.Application.Common;

namespace OpenFarm.Application.Assemblies;

public sealed class ListNonPrintedComponentsQueryHandler(INonPrintedComponentRepository repository)
    : IQueryHandler<ListNonPrintedComponentsQuery, NonPrintedComponentsPage>
{
    public async Task<NonPrintedComponentsPage> HandleAsync(ListNonPrintedComponentsQuery query, CancellationToken cancellationToken)
    {
        var (components, totalCount) = await repository.ListAsync(query.Page, query.PageSize, cancellationToken);

        var summaries = components
            .Select(c => new NonPrintedComponentSummary(c.Id, c.Name, c.Unit, c.UnitCost))
            .ToList();

        return new NonPrintedComponentsPage(summaries, totalCount);
    }
}
