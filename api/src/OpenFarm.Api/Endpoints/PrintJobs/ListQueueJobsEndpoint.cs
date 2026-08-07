using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class ListQueueJobsRequest
{
    public string View { get; set; } = "queue";

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 100;
}

public sealed class ListQueueJobsResponse
{
    public List<QueueJobRowResponse> Items { get; set; } = [];

    public int TotalCount { get; set; }
}

/// <summary>GET /api/v1/print-jobs?view=queue|backfill — the flat, QueuePosition-ordered "dispatch order" lens (FR-008).</summary>
[Permission("print-jobs:view")]
public sealed class ListQueueJobsEndpoint(IQueryHandler<GetQueueJobsQuery, QueueJobsPage> handler)
    : Endpoint<ListQueueJobsRequest, ListQueueJobsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/print-jobs");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListQueueJobsRequest req, CancellationToken ct)
    {
        var page = Math.Max(req.Page, 1);
        var pageSize = req.PageSize is < 1 or > 1000 ? 100 : req.PageSize;
        var backfillOnly = string.Equals(req.View, "backfill", StringComparison.OrdinalIgnoreCase);

        var result = await handler.HandleAsync(new GetQueueJobsQuery(page, pageSize, backfillOnly), ct);

        Response = new ListQueueJobsResponse
        {
            TotalCount = result.TotalCount,
            Items = result.Items.Select(QueueJobRowResponse.FromRow).ToList(),
        };
    }
}
