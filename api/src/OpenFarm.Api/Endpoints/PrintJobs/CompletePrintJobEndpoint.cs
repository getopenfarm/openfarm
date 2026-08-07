using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using CompletePrintJobHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.CompletePrintJobCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class FilamentConsumptionLineRequest
{
    public string? MaterialType { get; set; }

    public string? DetectedColorHex { get; set; }

    public Guid? SpoolId { get; set; }

    public decimal GramsConsumed { get; set; }
}

public sealed class CompletePrintJobRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public List<FilamentConsumptionLineRequest> FilamentConsumption { get; set; } = [];

    public string EntityType => "PrintJob";

    public string EventType => "PrintJobCompleted";
}

/// <summary>
/// POST /api/v1/print-jobs/{id}/complete — FR-013, manual-brand path (or, for an already-Completed
/// live-integrated job, just attaches the filament-consumption body — research.md R7). 400 if a
/// consumption line's (materialType, detectedColorHex) doesn't match one already on the job.
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
        var lines = req.FilamentConsumption
            .Select(l => new FilamentConsumptionLineInput(l.MaterialType, l.DetectedColorHex, l.SpoolId, l.GramsConsumed))
            .ToList();

        var job = await handler.HandleAsync(new CompletePrintJobCommand(req.Id, lines), ct);

        var detail = await getDetail.HandleAsync(new GetPrintJobQuery(job.Id), ct);
        Response = PrintJobResponse.FromDetail(detail!);
    }
}
