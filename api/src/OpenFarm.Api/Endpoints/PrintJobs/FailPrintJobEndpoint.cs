using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using FailPrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.FailPrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class FailPrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public List<FilamentConsumptionLineRequest> FilamentConsumption { get; set; } = [];

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobFailed";
}

/// <summary>Mirrors <see cref="CompletePrintJobEndpoint"/>'s shape (FR-013a) for the Failed outcome instead.</summary>
[Permission("print-jobs:update-status")]
public sealed class FailPrintJobEndpoint(FailPrintJobHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> getDetail)
    : Endpoint<FailPrintJobRequest, PrintJobResponse>
{
    public override void Configure()
    {
        Post("/api/v1/print-jobs/{id}/fail");
        Policies("Permission");
    }

    public override async Task HandleAsync(FailPrintJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var lines = req.FilamentConsumption
            .Select(l => new FilamentConsumptionLineInput(l.MaterialType, l.DetectedColorHex, l.SpoolId, l.GramsConsumed))
            .ToList();

        var job = await handler.HandleAsync(new FailPrintJobCommand(req.Id, lines), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
    }
}
