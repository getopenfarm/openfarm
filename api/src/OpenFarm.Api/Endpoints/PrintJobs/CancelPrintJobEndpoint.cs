using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using CancelPrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.CancelPrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class CancelPrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobCancelRequested";
}

/// <summary>
/// POST /api/v1/print-jobs/{id}/cancel — FR-024. Queued or manual-brand-Printing: applied
/// immediately (200). Live-integrated-Printing/Paused: stop requested, status unchanged until
/// telemetry confirms (202).
/// </summary>
[Permission("print-jobs:cancel")]
public sealed class CancelPrintJobEndpoint(CancelPrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<CancelPrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/cancel");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancelPrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new CancelPrintJobCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
        HttpContext.Response.StatusCode = job.Status == PrintJobStatus.Cancelled
            ? StatusCodes.Status200OK
            : StatusCodes.Status202Accepted;
    }
}
