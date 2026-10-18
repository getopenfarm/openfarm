using OpenFarm.Application.Common;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Denormalized joined read (contracts/filament-tracking-api.md) — every field computed at read time, never cached columns (plan.md's Constraints, SC-002).</summary>
public sealed class GetSpoolsQueryHandler(ISpoolReadModel spoolReadModel) : IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>>
{
    public async Task<IReadOnlyList<SpoolSummary>> HandleAsync(GetSpoolsQuery query, CancellationToken cancellationToken)
    {
        var page = await spoolReadModel.ListRowsAsync(query, cancellationToken);
        return page.Rows.Select(row => row.ToSummary()).ToList();
    }
}

/// <summary>The same read as <see cref="GetSpoolsQueryHandler"/>, keeping the total match count for paged callers.</summary>
public sealed class GetSpoolsPageQueryHandler(ISpoolReadModel spoolReadModel) : IQueryHandler<GetSpoolsQuery, SpoolSummaryPage>
{
    public async Task<SpoolSummaryPage> HandleAsync(GetSpoolsQuery query, CancellationToken cancellationToken)
    {
        var page = await spoolReadModel.ListRowsAsync(query, cancellationToken);
        return new SpoolSummaryPage(page.Rows.Select(row => row.ToSummary()).ToList(), page.TotalCount);
    }
}
