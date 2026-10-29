using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;
using EditAssemblyHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.EditAssemblyCommand, OpenFarm.Domain.Assemblies.Assembly>;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class EditAssemblyLineRequest
{
    /// <summary>An existing line's id to keep (quantity may change); omit to add a new line.</summary>
    public Guid? LineId { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public decimal Quantity { get; set; }
}

public sealed class EditAssemblyRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Full replacement of the BOM; omit (null) to rename/redescribe only.</summary>
    public List<EditAssemblyLineRequest>? Lines { get; set; }

    public List<AssemblyCostInput>? AdditionalCosts { get; set; }

    private AssemblyPricingDto? _pricing;

    /// <summary>Omit to keep the saved pricing; null clears it.</summary>
    public AssemblyPricingDto? Pricing
    {
        get => _pricing;
        set { _pricing = value; HasPricing = true; }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPricing { get; private set; }

    public string EntityType => "Assembly";

    public string EventType => "AssemblyEdited";
}

public sealed class EditAssemblyRequestValidator : AbstractValidator<EditAssemblyRequest>
{
    public EditAssemblyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().When(x => x.Lines is not null);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.TargetKind).Must(k => Enum.TryParse<BomLineTargetKind>(k, out _))
                .WithMessage("targetKind must be one of Part, Assembly, NonPrintedComponent.");
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
        RuleFor(x => x.Pricing!).SetValidator(new AssemblyPricingDtoValidator()).When(x => x.Pricing is not null);
    }
}

/// <summary>PUT /api/v1/assemblies/{id} — renames/redescribes and, when <c>lines</c> is sent, fully replaces the BOM in one all-or-nothing save.</summary>
[Permission("assemblies:edit")]
public sealed class EditAssemblyEndpoint(EditAssemblyHandler handler, IQueryHandler<GetAssemblyQuery, AssemblyDetail?> getAssembly)
    : Endpoint<EditAssemblyRequest, AssemblyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/assemblies/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditAssemblyRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var lines = req.Lines?.Select(l => new AssemblyLineEditInput(l.LineId, Enum.Parse<BomLineTargetKind>(l.TargetKind), l.TargetId, l.Quantity)).ToList();
        await handler.HandleAsync(new EditAssemblyCommand(req.Id, req.Name, req.Description, lines, req.AdditionalCosts, req.Pricing?.ToDomain(), req.HasPricing), ct);

        var detail = await getAssembly.HandleAsync(new GetAssemblyQuery(req.Id), ct);
        Response = AssemblyResponse.FromDetail(detail!);
    }
}
