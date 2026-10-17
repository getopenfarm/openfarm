using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using SetHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.SetPrintJobFilamentRequirementsCommand, OpenFarm.Domain.Production.PrintJob>;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class JobFilamentRequirementRequest
{
    public string? MaterialType { get; set; }
    public string? DetectedColorHex { get; set; }
    public Guid? NarrowFilamentVariantId { get; set; }
    public Guid? NarrowFilamentProfileId { get; set; }
}

public sealed class SetPrintJobFilamentRequirementsRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public List<JobFilamentRequirementRequest> Requirements { get; set; } = [];
    public string EntityType => "PrintJob";
    public string EventType => "PrintJobFilamentRequirementsChanged";
}
public sealed class SetPrintJobFilamentRequirementsRequestValidator : AbstractValidator<SetPrintJobFilamentRequirementsRequest>
{
    public SetPrintJobFilamentRequirementsRequestValidator()
    {
        RuleFor(r => r.Requirements).NotEmpty();
        RuleForEach(r => r.Requirements).Must(r => r.NarrowFilamentVariantId is null || r.NarrowFilamentProfileId is null)
            .WithMessage("Choose a catalog variant or a direct profile constraint, not both.");
    }
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
            req.Requirements.Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex, r.NarrowFilamentVariantId, r.NarrowFilamentProfileId)).ToList()), ct);
        Response = PrintJobResponse.FromDetail((await detail.HandleAsync(new GetPrintJobQuery(job.Id), ct))!);
    }
}
