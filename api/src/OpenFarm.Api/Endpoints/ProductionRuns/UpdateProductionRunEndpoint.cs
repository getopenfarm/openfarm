using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using UpdateProductionRunHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.UpdateProductionRunCommand, OpenFarm.Domain.Production.ProductionRun>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class RunLineBackfillLimitsRequest
{
    public Guid LineId { get; set; }

    public int BackfillPrinterCap { get; set; }

    public int? BackfillCumulativeUnitCeiling { get; set; }
}

public sealed class UpdateProductionRunRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Unlimited lines only; a line left out keeps its limits.</summary>
    public List<RunLineBackfillLimitsRequest> Lines { get; set; } = [];

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunEdited";
}

public sealed class UpdateProductionRunValidator : Validator<UpdateProductionRunRequest>
{
    public UpdateProductionRunValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.BackfillPrinterCap).GreaterThan(0);
            line.RuleFor(l => l.BackfillCumulativeUnitCeiling).Must(x => x is null or > 0);
        });
    }
}

/// <summary>PUT /api/v1/production-runs/{id} — an open run's name, due date, notes and backfill limits in one save. 409 when closed or cancelled, or for limits on a finite line.</summary>
[Permission("production-runs:edit")]
public sealed class UpdateProductionRunEndpoint(UpdateProductionRunHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getRun)
    : Endpoint<UpdateProductionRunRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Put("/api/v1/production-runs/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(UpdateProductionRunRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new UpdateProductionRunCommand(req.Id, req.Name, req.DueDate, req.Notes,
            req.Lines.Select(l => new RunLineBackfillLimitsInput(l.LineId, l.BackfillPrinterCap, l.BackfillCumulativeUnitCeiling)).ToList()), ct);

        var detail = await getRun.HandleAsync(new GetProductionRunDetailQuery(req.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
    }
}
