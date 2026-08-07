using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using PausePrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.PausePrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class PausePrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobPauseRequested";
}

/// <summary>
/// POST /api/v1/print-jobs/{id}/pause — FR-024, only valid on a Printing job. A live-integrated
/// printer's status stays "Printing" in the response (202, request accepted, telemetry confirms
/// later via FarmHub); a manual-brand job's response already reflects "Paused" (200).
/// </summary>
[Permission("print-jobs:remote-control")]
public sealed class PausePrintJobEndpoint(PausePrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<PausePrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/pause");
        Policies("Permission");
    }

    public override async Task HandleAsync(PausePrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new PausePrintJobCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
        HttpContext.Response.StatusCode = job.Status == PrintJobStatus.Paused
            ? StatusCodes.Status200OK
            : StatusCodes.Status202Accepted;
    }
}
