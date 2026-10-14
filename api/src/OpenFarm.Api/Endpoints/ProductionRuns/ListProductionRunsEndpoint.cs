using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class ListProductionRunsRequest
{
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed class ListProductionRunsResponse
{
    public List<ProductionRunSummaryResponse> Items { get; set; } = [];

    public int TotalCount { get; set; }
}

/// <summary>GET /api/v1/production-runs — paginated list (contracts/production-run-queue-api.md).</summary>
[Permission("production-runs:view")]
public sealed class ListProductionRunsEndpoint(IQueryHandler<GetProductionRunsQuery, ProductionRunsPage> handler)
    : Endpoint<ListProductionRunsRequest, ListProductionRunsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/production-runs");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListProductionRunsRequest req, CancellationToken ct)
    {
        var page = Math.Max(req.Page, 1);
        var pageSize = req.PageSize is < 1 or > 200 ? 25 : req.PageSize;

        var result = await handler.HandleAsync(new GetProductionRunsQuery(page, pageSize, ListSort.Parse(req.SortBy, req.SortDirection, "displayLabel")), ct);

        Response = new ListProductionRunsResponse
        {
            TotalCount = result.TotalCount,
            Items = result.Items.Select(ProductionRunSummaryResponse.FromSummary).ToList(),
        };
    }
}
