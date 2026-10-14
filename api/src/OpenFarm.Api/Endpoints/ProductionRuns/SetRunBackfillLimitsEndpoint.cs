using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using SetRunBackfillLimitsHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.SetRunBackfillLimitsCommand, OpenFarm.Domain.Production.ProductionRun>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class RunLineBackfillLimitsRequest
{
    public Guid LineId { get; set; }

    public int BackfillPrinterCap { get; set; }

    public int? BackfillCumulativeUnitCeiling { get; set; }
}

public sealed class SetRunBackfillLimitsRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public List<RunLineBackfillLimitsRequest> Lines { get; set; } = [];

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunBackfillLimitsChanged";
}

public sealed class SetRunBackfillLimitsValidator : Validator<SetRunBackfillLimitsRequest>
{
    public SetRunBackfillLimitsValidator()
    {
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.BackfillPrinterCap).GreaterThan(0);
            line.RuleFor(l => l.BackfillCumulativeUnitCeiling).Must(x => x is null or > 0);
        });
    }
}

/// <summary>PUT /api/v1/production-runs/{id}/backfill-limits — FR-006c/d for many unlimited lines in one save; 409 if any listed line has a quantity.</summary>
[Permission("production-runs:edit")]
public sealed class SetRunBackfillLimitsEndpoint(SetRunBackfillLimitsHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getRun)
    : Endpoint<SetRunBackfillLimitsRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Put("/api/v1/production-runs/{id}/backfill-limits");
        Policies("Permission");
    }

    public override async Task HandleAsync(SetRunBackfillLimitsRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(
            new SetRunBackfillLimitsCommand(req.Id, req.Lines.Select(l => new RunLineBackfillLimitsInput(l.LineId, l.BackfillPrinterCap, l.BackfillCumulativeUnitCeiling)).ToList()), ct);

        var detail = await getRun.HandleAsync(new GetProductionRunDetailQuery(req.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
    }
}
