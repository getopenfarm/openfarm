using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

/// <summary>GET /api/v1/production-runs/{id} — one level of line/requirement summary (contracts/production-run-queue-api.md).</summary>
[Permission("production-runs:view")]
public sealed class GetProductionRunEndpoint(IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> handler)
    : EndpointWithoutRequest<ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Get("/api/v1/production-runs/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetProductionRunDetailQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = ProductionRunDetailResponse.FromView(detail);
    }
}
