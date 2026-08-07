using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using UnpinPrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.UnpinPrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class UnpinPrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobUnpinned";
}

/// <summary>POST /api/v1/print-jobs/{id}/unpin — reverts to Unassigned. Same 404/409 shape as pin.</summary>
[Permission("print-jobs:pin")]
public sealed class UnpinPrintJobEndpoint(UnpinPrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<UnpinPrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/unpin");
        Policies("Permission");
    }

    public override async Task HandleAsync(UnpinPrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new UnpinPrintJobCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
    }
}
