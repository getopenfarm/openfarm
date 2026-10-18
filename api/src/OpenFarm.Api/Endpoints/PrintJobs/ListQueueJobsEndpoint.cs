using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class ListQueueJobsRequest
{
    public string View { get; set; } = "queue";

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 100;

    public string? Status { get; set; }

    public string? Search { get; set; }
}

public sealed class ListQueueJobsValidator : Validator<ListQueueJobsRequest>
{
    public ListQueueJobsValidator()
    {
        RuleFor(x => x.Status).Must(status => status is null ||
            string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ||
            Enum.GetNames<PrintJobStatus>().Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Status must be Active, Queued, Printing, Paused, Completed, Failed or Cancelled.");
    }
}

public sealed class ListQueueJobsResponse
{
    public List<QueueJobRowResponse> Items { get; set; } = [];

    public int TotalCount { get; set; }

    public IReadOnlyDictionary<string, int> StatusCounts { get; set; } = new Dictionary<string, int>();
}

/// <summary>GET /api/v1/print-jobs?view=queue|backfill|external — the flat, QueuePosition-ordered "dispatch order" lens (FR-008); "external" lists prints the printer started itself, newest first.</summary>
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
        var externalOnly = string.Equals(req.View, "external", StringComparison.OrdinalIgnoreCase);

        var result = await handler.HandleAsync(new GetQueueJobsQuery(page, pageSize, backfillOnly, req.Status, req.Search, externalOnly), ct);

        Response = new ListQueueJobsResponse
        {
            TotalCount = result.TotalCount,
            StatusCounts = result.StatusCounts,
            Items = result.Items.Select(QueueJobRowResponse.FromRow).ToList(),
        };
    }
}
