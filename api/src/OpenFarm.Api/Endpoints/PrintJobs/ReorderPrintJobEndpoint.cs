using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using ReorderPrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.ReorderPrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class ReorderPrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public Guid? AfterJobId { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobReordered";
}

public sealed class ReorderPrintJobResponse
{
    public Guid JobId { get; set; }

    public double QueuePosition { get; set; }
}

/// <summary>POST /api/v1/print-jobs/{id}/reorder — FR-008, the fractional-index move (research.md R3). 404 on missing job/afterJobId, 409 if no longer Queued.</summary>
[Permission("print-jobs:reorder")]
public sealed class ReorderPrintJobEndpoint(ReorderPrintJobHandler handler)
    : Endpoint<ReorderPrintJobRequest, ReorderPrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/reorder");
        Policies("Permission");
    }

    public override async Task HandleAsync(ReorderPrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(
            new OpenFarm.Application.Production.ReorderPrintJobCommand(req.Id, req.AfterJobId), ct);

        Response = new ReorderPrintJobResponse { JobId = job.Id, QueuePosition = job.QueuePosition };
    }
}
