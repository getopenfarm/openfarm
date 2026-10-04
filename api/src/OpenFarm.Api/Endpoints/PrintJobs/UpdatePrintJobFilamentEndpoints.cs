using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using SetHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.SetPrintJobFilamentRequirementsCommand, OpenFarm.Domain.Production.PrintJob>;
using RecordHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.RecordPrintJobFilamentConsumptionCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class SetPrintJobFilamentRequirementsRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public List<FilamentConsumptionLineRequest> Requirements { get; set; } = [];
    public string EntityType => "PrintJob";
    public string EventType => "PrintJobFilamentRequirementsChanged";
}
public sealed class SetPrintJobFilamentRequirementsRequestValidator : AbstractValidator<SetPrintJobFilamentRequirementsRequest>
{
    public SetPrintJobFilamentRequirementsRequestValidator() => RuleFor(r => r.Requirements).NotEmpty();
}

[Permission("print-jobs:pin")]
public sealed class SetPrintJobFilamentRequirementsEndpoint(SetHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> detail)
    : Endpoint<SetPrintJobFilamentRequirementsRequest, PrintJobResponse>
{
    public override void Configure() { Put("/api/v1/print-jobs/{id}/filament-requirements"); Policies("Permission"); }
    public override async Task HandleAsync(SetPrintJobFilamentRequirementsRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new SetPrintJobFilamentRequirementsCommand(req.Id,
            req.Requirements.Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex)).ToList()), ct);
        Response = PrintJobResponse.FromDetail((await detail.HandleAsync(new GetPrintJobQuery(job.Id), ct))!);
    }
}

public sealed class RecordPrintJobFilamentConsumptionRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public List<FilamentConsumptionLineRequest> FilamentConsumption { get; set; } = [];
    public string EntityType => "PrintJob";
    public string EventType => "PrintJobFilamentConsumptionRecorded";
}
public sealed class RecordPrintJobFilamentConsumptionRequestValidator : AbstractValidator<RecordPrintJobFilamentConsumptionRequest>
{
    public RecordPrintJobFilamentConsumptionRequestValidator()
    {
        RuleFor(r => r.FilamentConsumption).NotEmpty();
        RuleForEach(r => r.FilamentConsumption).ChildRules(line => line.RuleFor(l => l.GramsConsumed).GreaterThanOrEqualTo(0));
    }
}

[Permission("print-jobs:update-status")]
public sealed class RecordPrintJobFilamentConsumptionEndpoint(RecordHandler handler, IQueryHandler<GetPrintJobQuery, PrintJobDetail?> detail)
    : Endpoint<RecordPrintJobFilamentConsumptionRequest, PrintJobResponse>
{
    public override void Configure() { Post("/api/v1/print-jobs/{id}/filament-consumption"); Policies("Permission"); }
    public override async Task HandleAsync(RecordPrintJobFilamentConsumptionRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var job = await handler.HandleAsync(new RecordPrintJobFilamentConsumptionCommand(req.Id,
            req.FilamentConsumption.Select(r => new FilamentConsumptionLineInput(r.MaterialType, r.DetectedColorHex, r.SpoolId, r.GramsConsumed)).ToList()), ct);
        Response = PrintJobResponse.FromDetail((await detail.HandleAsync(new GetPrintJobQuery(job.Id), ct))!);
    }
}
