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

    public string? RevisionMode { get; set; }

    public string AssignmentMode { get; set; } = "Unassigned";

    public Guid? PinnedPrinterId { get; set; }

    public int BackfillPrinterCap { get; set; } = 1;

    public int? BackfillCumulativeUnitCeiling { get; set; }
}

public sealed class CreateProductionRunRequest : IActivityLoggable
{
    public string? Name { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Priority { get; set; }

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
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.TargetKind).Must(k => Enum.TryParse<RunLineTargetKind>(k, out _))
                .WithMessage("targetKind must be one of Part, Assembly.");
            line.RuleFor(l => l.Quantity).GreaterThanOrEqualTo(1).When(l => l.Quantity is not null);
            line.RuleFor(l => l.PinnedPrinterId).NotNull()
                .When(l => l.AssignmentMode == "Pinned")
                .WithMessage("pinnedPrinterId is required when assignmentMode is Pinned.");
        });
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
        var lines = req.Lines
            .Select(l => new RunLineInput(
                Enum.Parse<RunLineTargetKind>(l.TargetKind),
                l.TargetId,
                l.Quantity,
                l.RevisionMode is null ? null : Enum.Parse<RevisionMode>(l.RevisionMode),
                Enum.Parse<AssignmentMode>(l.AssignmentMode),
                l.PinnedPrinterId,
                l.BackfillPrinterCap,
                l.BackfillCumulativeUnitCeiling))
            .ToList();

        var run = await handler.HandleAsync(new CreateProductionRunCommand(req.Name, req.DueDate, req.Priority, req.Notes, lines), ct);

        var detail = await getDetail.HandleAsync(new GetProductionRunDetailQuery(run.Id), ct);
        Response = ProductionRunDetailResponse.FromView(detail!);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
