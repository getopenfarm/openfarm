using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using SetRunLineBackfillLimitsHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.SetRunLineBackfillLimitsCommand, OpenFarm.Domain.Production.RunLine>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class SetRunLineBackfillLimitsRequest : IActivityLoggable
{
    public Guid LineId { get; set; }

    public int BackfillPrinterCap { get; set; }

    public int? BackfillCumulativeUnitCeiling { get; set; }

    public string EntityType => "RunLine";

    public string EventType => "RunLineBackfillLimitsChanged";
}

public sealed class RunLineBackfillLimitsResponse
{
    public Guid Id { get; set; }

    public int BackfillPrinterCap { get; set; }

    public int? BackfillCumulativeUnitCeiling { get; set; }
}

/// <summary>PUT /api/v1/production-runs/{runId}/lines/{lineId}/backfill-limits — FR-006c/d, only valid on a line with quantity: null. 409 otherwise.</summary>
[Permission("production-runs:edit")]
public sealed class SetRunLineBackfillLimitsEndpoint(SetRunLineBackfillLimitsHandler handler)
    : Endpoint<SetRunLineBackfillLimitsRequest, RunLineBackfillLimitsResponse>
{
    public override void Configure()
    {
        Put("/api/v1/production-runs/{runId}/lines/{lineId}/backfill-limits");
        Policies("Permission");
    }

    public override async Task HandleAsync(SetRunLineBackfillLimitsRequest req, CancellationToken ct)
    {
        req.LineId = Route<Guid>("lineId");
        var line = await handler.HandleAsync(
            new OpenFarm.Application.Production.SetRunLineBackfillLimitsCommand(req.LineId, req.BackfillPrinterCap, req.BackfillCumulativeUnitCeiling), ct);

        Response = new RunLineBackfillLimitsResponse
        {
            Id = line.Id,
            BackfillPrinterCap = line.BackfillPrinterCap,
            BackfillCumulativeUnitCeiling = line.BackfillCumulativeUnitCeiling,
        };
    }
}
