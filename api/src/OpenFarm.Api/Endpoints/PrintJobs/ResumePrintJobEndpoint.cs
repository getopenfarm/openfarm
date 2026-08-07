using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using ResumePrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.ResumePrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class ResumePrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobResumeRequested";
}

/// <summary>Mirrors <see cref="PausePrintJobEndpoint"/>'s request/confirm split — only valid on a Paused job.</summary>
[Permission("print-jobs:remote-control")]
public sealed class ResumePrintJobEndpoint(ResumePrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<ResumePrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/resume");
        Policies("Permission");
    }

    public override async Task HandleAsync(ResumePrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new ResumePrintJobCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
        HttpContext.Response.StatusCode = job.Status == PrintJobStatus.Printing
            ? StatusCodes.Status200OK
            : StatusCodes.Status202Accepted;
    }
}
