using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using CompletePrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.CompletePrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class CompletePrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobCompleted";
}

/// <summary>
/// POST /api/v1/print-jobs/{id}/complete — FR-013, manual-brand path. Deducts the job's full filament estimate.
/// </summary>
[Permission("print-jobs:update-status")]
public sealed class CompletePrintJobEndpoint(CompletePrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<CompletePrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/complete");
        Policies("Permission");
    }

    public override async Task HandleAsync(CompletePrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new CompletePrintJobCommand(req.Id), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
    }
}
