using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using WriteOffPrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.WriteOffPrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class WriteOffPrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobWrittenOff";
}

/// <summary>POST /api/v1/print-jobs/{id}/write-off — FR-025's other remedy: accept a lost plate instead of reprinting it. 409 unless failed or cancelled, finite, and not yet requeued or written off.</summary>
[Permission("print-jobs:requeue")]
public sealed class WriteOffPrintJobEndpoint(WriteOffPrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<WriteOffPrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/write-off");
        Policies("Permission");
    }

    public override async Task HandleAsync(WriteOffPrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new WriteOffPrintJobCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
    }
}
