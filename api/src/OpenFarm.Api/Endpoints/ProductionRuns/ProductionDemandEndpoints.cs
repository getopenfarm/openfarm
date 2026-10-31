using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class PreviewProductionPlanRequest
{
    public CreateRunLineRequest Line { get; set; } = new();
    public Dictionary<Guid, int>? PlateOverrides { get; set; }
    public ProductionPlanInput ToInput() => new(Enum.Parse<RunLineTargetKind>(Line.TargetKind), Line.TargetId,
        Line.Quantity ?? 1, Line.QuantityBasis is null ? (Line.TargetKind == "Part" ? QuantityBasis.Plates : QuantityBasis.Sets)
            : Enum.Parse<QuantityBasis>(Line.QuantityBasis), PlateOverrides);
}
public sealed class PreviewProductionPlanValidator : Validator<PreviewProductionPlanRequest>
{
    public PreviewProductionPlanValidator() => RuleFor(r => r.Line).SetValidator(new CreateRunLineRequestValidator());
}
[Permission("production-runs:create")]
public sealed class PreviewProductionPlanEndpoint(ProductionPlanner planner) : Endpoint<PreviewProductionPlanRequest, ProductionPlan>
{
    public override void Configure() { Post("/api/v1/production-demands/preview"); Policies("Permission"); }
    public override async Task HandleAsync(PreviewProductionPlanRequest req, CancellationToken ct) => Response = await planner.PlanAsync(req.ToInput(), ct);
}
public sealed class SubmitProductionDemandRequest : IActivityLoggable
{
    public Guid SubmissionId { get; set; }
    public CreateRunLineRequest Line { get; set; } = new();
    public string? PlanVersion { get; set; }
    public Guid? ProductionRunId { get; set; }
    public bool CreateRun { get; set; }
    public string? NewRunName { get; set; }
    public Dictionary<Guid, int>? PlateOverrides { get; set; }
    public string EntityType => "ProductionDemand";
    public string EventType => "ProductionDemandCreated";
}
public sealed class SubmitProductionDemandValidator : Validator<SubmitProductionDemandRequest>
{
    public SubmitProductionDemandValidator()
    {
        RuleFor(r => r.SubmissionId).NotEmpty();
        RuleFor(r => r.Line).SetValidator(new CreateRunLineRequestValidator());
        RuleFor(r => r.PlanVersion).NotEmpty().When(r => r.Line.Quantity is not null);
    }
}
public sealed record ProductionDemandResponse(Guid Id, Guid? ProductionRunId, string Name, string Mode,
    string State, int PrinterCap, Guid? PinnedPrinterId, string TargetKind, Guid TargetId)
{
    public static ProductionDemandResponse From(ProductionDemand d) => new(d.Id, d.ProductionRunId, d.TargetName ?? "Print demand",
        d.Mode.ToString(), d.State.ToString(), d.BackfillPrinterCap, d.DefaultPinnedPrinterId, d.TargetKind.ToString(), d.TargetId);
}
[Permission("production-runs:create")]
public sealed class SubmitProductionDemandEndpoint(ProductionDemandService service) : Endpoint<SubmitProductionDemandRequest, ProductionDemandResponse>
{
    public override void Configure() { Post("/api/v1/production-demands"); Policies("Permission"); }
    public override async Task HandleAsync(SubmitProductionDemandRequest req, CancellationToken ct)
    {
        var result = await service.SubmitAsync(new(req.SubmissionId, req.Line.ToInput(), req.PlanVersion,
            req.ProductionRunId, req.NewRunName, req.CreateRun, req.PlateOverrides), ct);
        Response = ProductionDemandResponse.From(result);
    }
}
[Permission("production-runs:view")]
public sealed class ListBackfillEndpoint(IProductionDemandRepository demands) : EndpointWithoutRequest<List<ProductionDemandResponse>>
{
    public override void Configure() { Get("/api/v1/production-demands/backfill"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = (await demands.ListBackfillAsync(ct)).Select(ProductionDemandResponse.From).ToList();
}
public sealed class ChangeBackfillRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public string Action { get; set; } = "";
    public int? PrinterCap { get; set; }
    public Guid? PinnedPrinterId { get; set; }
    public string EntityType => "ProductionDemand";
    public string EventType => "BackfillChanged";
}
public sealed class ChangeBackfillValidator : Validator<ChangeBackfillRequest>
{
    public ChangeBackfillValidator() => RuleFor(r => r.Action).Must(a => a is "Enable" or "Pause" or "Remove" or "Refresh" or "Configure");
}
[Permission("production-runs:edit")]
public sealed class ChangeBackfillEndpoint(ProductionDemandService service) : Endpoint<ChangeBackfillRequest, ProductionDemandResponse>
{
    public override void Configure() { Post("/api/v1/production-demands/{id}/backfill"); Policies("Permission"); }
    public override async Task HandleAsync(ChangeBackfillRequest req, CancellationToken ct) =>
        Response = ProductionDemandResponse.From(await service.ChangeBackfillAsync(Route<Guid>("id"), req.Action, req.PrinterCap, req.PinnedPrinterId, ct));
}
