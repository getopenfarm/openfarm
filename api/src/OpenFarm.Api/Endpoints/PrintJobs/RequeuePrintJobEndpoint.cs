using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using RequeuePrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.RequeuePrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class RequeuePrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobRequeued";
}

/// <summary>POST /api/v1/print-jobs/{id}/requeue — FR-025a, the one remedy for a shortfall. 409 if not Cancelled/Failed, already requeued, or on an unlimited requirement.</summary>
[Permission("print-jobs:requeue")]
public sealed class RequeuePrintJobEndpoint(RequeuePrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<RequeuePrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/requeue");
        Policies("Permission");
    }

    public override async Task HandleAsync(RequeuePrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var replacement = await handler.HandleAsync(new RequeuePrintJobCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(replacement.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
