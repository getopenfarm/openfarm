using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using CloseProductionRunHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.CloseProductionRunCommand, OpenFarm.Domain.Production.ProductionRun>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class CloseProductionRunRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunClosed";
}

/// <summary>POST /api/v1/production-runs/{id}/close — FR-016c, the operator's sign-off. 409 unless Complete, or Active with no finite work left.</summary>
[Permission("production-runs:close")]
public sealed class CloseProductionRunEndpoint(CloseProductionRunHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getDetail)
    : Endpoint<CloseProductionRunRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Post("/api/v1/production-runs/{id}/close");
        Policies("Permission");
    }

    public override async Task HandleAsync(CloseProductionRunRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var run = await handler.HandleAsync(new CloseProductionRunCommand(req.Id, Guid.TryParse(User.FindFirst("sub")?.Value, out var actorId) ? actorId : null), ct);

        var detail = await getDetail.HandleAsync(new GetProductionRunDetailQuery(run.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
    }
}
