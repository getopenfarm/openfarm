using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using PinPrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.PinPrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class PinPrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public Guid? PrinterId { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobPinned";
}

/// <summary>POST /api/v1/print-jobs/{id}/pin — FR-007. 400 (PinnedJobRequiresPrinterException) if printerId is missing, 404 on missing job/printer, 409 if not Queued.</summary>
[Permission("print-jobs:pin")]
public sealed class PinPrintJobEndpoint(PinPrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<PinPrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/pin");
        Policies("Permission");
    }

    public override async Task HandleAsync(PinPrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new PinPrintJobCommand(req.Id, req.PrinterId), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
    }
}
