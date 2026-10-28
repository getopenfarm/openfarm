using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using AddRunLinesHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.AddRunLinesCommand, OpenFarm.Domain.Production.ProductionRun>;
using MoveRunToTopHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.MoveRunToTopCommand, OpenFarm.Domain.Production.ProductionRun>;
using RequeueFailedJobsHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.RequeueFailedJobsCommand, int>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class AddRunLinesRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public List<CreateRunLineRequest> Lines { get; set; } = [];

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunLinesAdded";
}

public sealed class AddRunLinesRequestValidator : Validator<AddRunLinesRequest>
{
    public AddRunLinesRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new CreateRunLineRequestValidator());
    }
}

/// <summary>POST /api/v1/production-runs/{id}/lines — more parts or assemblies on an open run, queued after everything waiting. 409 when closed or cancelled.</summary>
[Permission("production-runs:edit")]
public sealed class AddRunLinesEndpoint(
    AddRunLinesHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getRun)
    : Endpoint<AddRunLinesRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Post("/api/v1/production-runs/{id}/lines");
        Policies("Permission");
    }

    public override async Task HandleAsync(AddRunLinesRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new AddRunLinesCommand(req.Id, req.Lines.Select(l => l.ToInput()).ToList()), ct);

        var detail = await getRun.HandleAsync(new GetProductionRunDetailQuery(req.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
    }
}

public sealed class MoveRunToTopRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunMovedToTop";
}

/// <summary>POST /api/v1/production-runs/{id}/move-to-top — the run's waiting jobs move above everything else in the queue, as one block.</summary>
[Permission("print-jobs:reorder")]
public sealed class MoveRunToTopEndpoint(
    MoveRunToTopHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getRun)
    : Endpoint<MoveRunToTopRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Post("/api/v1/production-runs/{id}/move-to-top");
        Policies("Permission");
    }

    public override async Task HandleAsync(MoveRunToTopRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new MoveRunToTopCommand(req.Id), ct);

        var detail = await getRun.HandleAsync(new GetProductionRunDetailQuery(req.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
    }
}

public sealed class RequeueFailedJobsRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunLostPlatesRequeued";
}

/// <summary>POST /api/v1/production-runs/{id}/requeue-failed — reprints every lost plate that still has a snapshot. 409 when closed or cancelled.</summary>
[Permission("print-jobs:requeue")]
public sealed class RequeueFailedJobsEndpoint(
    RequeueFailedJobsHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getRun)
    : Endpoint<RequeueFailedJobsRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Post("/api/v1/production-runs/{id}/requeue-failed");
        Policies("Permission");
    }

    public override async Task HandleAsync(RequeueFailedJobsRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new RequeueFailedJobsCommand(req.Id), ct);

        var detail = await getRun.HandleAsync(new GetProductionRunDetailQuery(req.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
    }
}
