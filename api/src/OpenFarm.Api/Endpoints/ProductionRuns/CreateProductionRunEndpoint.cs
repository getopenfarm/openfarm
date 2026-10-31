using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using CreateProductionRunHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.CreateProductionRunCommand, OpenFarm.Domain.Production.ProductionRun>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class CreateRunLineRequest
{
    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public int? Quantity { get; set; }

    public string? PlanVersion { get; set; }

    public string? QuantityBasis { get; set; }

    public string? RevisionMode { get; set; }

    public string AssignmentMode { get; set; } = "Unassigned";

    public Guid? PinnedPrinterId { get; set; }

    public int BackfillPrinterCap { get; set; } = 1;

    public int? BackfillCumulativeUnitCeiling { get; set; }

    public RunLineInput ToInput() => new(
        Enum.Parse<RunLineTargetKind>(TargetKind),
        TargetId,
        Quantity,
        RevisionMode is null ? null : Enum.Parse<RevisionMode>(RevisionMode),
        Enum.Parse<AssignmentMode>(AssignmentMode),
        PinnedPrinterId,
        BackfillPrinterCap,
        BackfillCumulativeUnitCeiling, QuantityBasis is null ? null : Enum.Parse<QuantityBasis>(QuantityBasis), PlanVersion);
}

public sealed class CreateRunLineRequestValidator : AbstractValidator<CreateRunLineRequest>
{
    public CreateRunLineRequestValidator()
    {
        RuleFor(l => l.TargetKind).Must(k => Enum.GetNames<RunLineTargetKind>().Contains(k))
            .WithMessage("targetKind must be one of Part, Assembly.");
        RuleFor(l => l.AssignmentMode).Must(k => Enum.GetNames<AssignmentMode>().Contains(k));
        RuleFor(l => l.RevisionMode).Must(k => k is null || (Enum.GetNames<RevisionMode>().Contains(k)));
        RuleFor(l => l.TargetId).NotEmpty();
        RuleFor(l => l.QuantityBasis).Must(k => k is null || Enum.GetNames<QuantityBasis>().Contains(k));
        RuleFor(l => l).Must(l => l.QuantityBasis is null || (l.TargetKind == "Assembly" ? l.QuantityBasis == "Sets" : l.QuantityBasis != "Sets"))
            .WithMessage("Quantity basis must match the target.");
        RuleFor(l => l.BackfillPrinterCap).GreaterThanOrEqualTo(1);
        RuleFor(l => l.BackfillCumulativeUnitCeiling).GreaterThanOrEqualTo(1).When(l => l.BackfillCumulativeUnitCeiling is not null);
        RuleFor(l => l.Quantity).GreaterThanOrEqualTo(1).When(l => l.Quantity is not null);
        RuleFor(l => l.PinnedPrinterId).NotNull()
            .When(l => l.AssignmentMode == "Pinned")
            .WithMessage("pinnedPrinterId is required when assignmentMode is Pinned.");
    }
}

public sealed class CreateProductionRunRequest : IActivityLoggable
{
    public Guid? SubmissionId { get; set; }

    public string? Name { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Notes { get; set; }

    public List<CreateRunLineRequest> Lines { get; set; } = [];

    public string EntityType => "ProductionRun";

    public string EventType => "ProductionRunCreated";
}

public sealed class CreateProductionRunRequestValidator : AbstractValidator<CreateProductionRunRequest>
{
    public CreateProductionRunRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new CreateRunLineRequestValidator());
    }
}

/// <summary>FR-001/FR-003/FR-005b — creates a run, snapshots BOM explosion, eagerly creates finite lines' jobs (contracts/production-run-queue-api.md).</summary>
[Permission("production-runs:create")]
public sealed class CreateProductionRunEndpoint(
    CreateProductionRunHandler handler, IQueryHandler<GetProductionRunDetailQuery, RunDetailView?> getDetail)
    : Endpoint<CreateProductionRunRequest, ProductionRunDetailResponse>
{
    public override void Configure()
    {
        Post("/api/v1/production-runs");
        Policies("Permission");
    }

    public override async Task HandleAsync(CreateProductionRunRequest req, CancellationToken ct)
    {
        var run = await handler.HandleAsync(new CreateProductionRunCommand(req.Name, req.DueDate, req.Notes, req.Lines.Select(l => l.ToInput()).ToList(), req.SubmissionId), ct);

        var detail = await getDetail.HandleAsync(new GetProductionRunDetailQuery(run.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
