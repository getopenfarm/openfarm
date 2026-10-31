using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using EditPartHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.EditPartCommand, OpenFarm.Domain.Parts.Part>;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class EditPartFilamentRequirementRequest
{
    public Guid RequirementId { get; set; }

    public Guid? NarrowFilamentVariantId { get; set; }

    public Guid? NarrowFilamentProfileId { get; set; }
}

public sealed class EditPartRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public int UnitsPerPrint { get; set; }

    public List<EditPartFilamentRequirementRequest> FilamentRequirements { get; set; } = [];

    public string EntityType => "Part";

    public string EventType => "PartEdited";
}

public sealed class EditPartRequestValidator : AbstractValidator<EditPartRequest>
{
    public EditPartRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.UnitsPerPrint).GreaterThanOrEqualTo(1);
        RuleForEach(x => x.FilamentRequirements)
            .Must(r => r.NarrowFilamentVariantId is null || r.NarrowFilamentProfileId is null)
            .WithMessage("At most one of narrowFilamentVariantId/narrowFilamentProfileId may be set.");
    }
}

/// <summary>PUT /api/v1/parts/{id} — name, tags, units per print and filament narrowing in one all-or-nothing save.</summary>
[Permission("parts:edit")]
public sealed class EditPartEndpoint(EditPartHandler handler, IQueryHandler<GetPartQuery, PartDetail?> getPart)
    : Endpoint<EditPartRequest, PartResponse>
{
    public override void Configure()
    {
        Put("/api/v1/parts/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditPartRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var command = new EditPartCommand(
            req.Id,
            req.Name,
            req.Tags,
            req.UnitsPerPrint,
            req.FilamentRequirements.Select(r => new PartFilamentRequirementNarrowingInput(r.RequirementId, r.NarrowFilamentVariantId, r.NarrowFilamentProfileId)).ToList());
        await handler.HandleAsync(command, ct);

        var detail = await getPart.HandleAsync(new GetPartQuery(req.Id), ct);
        Response = PartResponse.FromDetail(detail!);
    }
}
