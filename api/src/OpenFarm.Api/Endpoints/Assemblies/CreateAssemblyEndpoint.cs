using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;
using CreateAssemblyHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.CreateAssemblyCommand, OpenFarm.Domain.Assemblies.Assembly>;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class CreateAssemblyLineRequest
{
    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public decimal Quantity { get; set; }
}

public sealed class CreateAssemblyRequest : IActivityLoggable
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<CreateAssemblyLineRequest> Lines { get; set; } = [];

    public List<AssemblyCostInput>? AdditionalCosts { get; set; }

    public AssemblyPricingDto? Pricing { get; set; }

    public string EntityType => "Assembly";

    public string EventType => "AssemblyCreated";
}

public sealed class CreateAssemblyRequestValidator : AbstractValidator<CreateAssemblyRequest>
{
    public CreateAssemblyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.TargetKind).Must(k => Enum.TryParse<BomLineTargetKind>(k, out _))
                .WithMessage("targetKind must be one of Part, Assembly, NonPrintedComponent.");
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
        RuleFor(x => x.Pricing!).SetValidator(new AssemblyPricingDtoValidator()).When(x => x.Pricing is not null);
    }
}

/// <summary>FR-001/FR-002 — creates a named assembly from one or more BOM lines (contracts/assembly-bom-api.md).</summary>
[Permission("assemblies:create")]
public sealed class CreateAssemblyEndpoint(CreateAssemblyHandler handler, IQueryHandler<GetAssemblyQuery, AssemblyDetail?> getAssembly)
    : Endpoint<CreateAssemblyRequest, AssemblyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/assemblies");
        Policies("Permission");
    }

    public override async Task HandleAsync(CreateAssemblyRequest req, CancellationToken ct)
    {
        var lines = req.Lines
            .Select(l => new AssemblyLineInput(Enum.Parse<BomLineTargetKind>(l.TargetKind), l.TargetId, l.Quantity))
            .ToList();
        var assembly = await handler.HandleAsync(new CreateAssemblyCommand(req.Name, req.Description, lines, req.AdditionalCosts, req.Pricing?.ToDomain()), ct);

        var detail = await getAssembly.HandleAsync(new GetAssemblyQuery(assembly.Id), ct);
        Response = AssemblyResponse.FromDetail(detail!);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
