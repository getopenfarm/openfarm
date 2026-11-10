using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Mcp.Tools;

public static class ProductionRunTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_production_runs", "List production runs",
            "Production runs with their lines, unit progress and plate counts, plus counts per status. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Open (Active or Complete), Closed or Cancelled; omit for every run")] string? status = null,
                [Description("Matches the run name or a line's target name")] string? search = null,
                [Description("1-based page, default 1")] int? page = null,
                [Description("1–100, default 25")] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                new ListProductionRunsValidator().ValidateAndThrow(new ListProductionRunsRequest { Status = status });
                return ListProductionRunsResponse.From(await request.Service<IQueryHandler<GetProductionRunsQuery, ProductionRunsPage>>()
                    .HandleAsync(new GetProductionRunsQuery(McpTools.Page(page), McpTools.PageSize(pageSize), Status: status, Search: search), ct));
            },
            "production-runs:view"),
        McpTools.Read("get_production_run", "Get production run",
            "One production run: its lines with their part requirements and jobs.",
            async (RequestContext<CallToolRequestParams> request, [Description("Production run id")] Guid productionRunId, CancellationToken ct) =>
                ProductionRunDetailResponse.FromView(McpTools.Found(await request.Service<IQueryHandler<GetProductionRunDetailQuery, RunDetailView?>>()
                    .HandleAsync(new GetProductionRunDetailQuery(productionRunId), ct), "Production run")),
            "production-runs:view"),
    ];
}
