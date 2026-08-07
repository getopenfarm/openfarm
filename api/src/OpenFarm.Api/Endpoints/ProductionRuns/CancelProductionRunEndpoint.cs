using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using CancelProductionRunHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.CancelProductionRunCommand, OpenFarm.Domain.Production.ProductionRun>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class CancelProductionRunRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunCancelled";
}

/// <summary>POST /api/v1/production-runs/{id}/cancel — FR-017. Cancels every queued, undispatched job; a job already printing is untouched.</summary>
[Permission("production-runs:cancel")]
public sealed class CancelProductionRunEndpoint(CancelProductionRunHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getDetail)
    : Endpoint<CancelProductionRunRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Post("/api/v1/production-runs/{id}/cancel");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancelProductionRunRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var run = await handler.HandleAsync(new CancelProductionRunCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetProductionRunDetailQuery(run.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
    }
}
