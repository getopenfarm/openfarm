using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using MarkPrintJobStartedHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.MarkPrintJobStartedCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class MarkPrintJobStartedRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobMarkedStarted";
}

/// <summary>POST /api/v1/print-jobs/{id}/mark-started — FR-012's manual-brand path. 409 if the job's printer has live integration, or it isn't dispatched-but-not-started.</summary>
[Permission("print-jobs:update-status")]
public sealed class MarkPrintJobStartedEndpoint(MarkPrintJobStartedHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<MarkPrintJobStartedRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/mark-started");
        Policies("Permission");
    }

    public override async Task HandleAsync(MarkPrintJobStartedRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new MarkPrintJobStartedCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
    }
}
