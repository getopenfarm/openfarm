using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.ActivityLog;
using UpdatePartFilamentRequirementNarrowingHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Parts.UpdatePartFilamentRequirementNarrowingCommand, OpenFarm.Domain.Parts.PartFilamentRequirement>;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class UpdatePartFilamentRequirementNarrowingRequest
{
    public Guid? NarrowFilamentVariantId { get; set; }

    public Guid? NarrowFilamentProfileId { get; set; }
}

public sealed class UpdatePartFilamentRequirementNarrowingRequestValidator : AbstractValidator<UpdatePartFilamentRequirementNarrowingRequest>
{
    public UpdatePartFilamentRequirementNarrowingRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.NarrowFilamentVariantId is null || x.NarrowFilamentProfileId is null)
            .WithMessage("At most one of narrowFilamentVariantId/narrowFilamentProfileId may be set.");
    }
}

/// <summary>
/// Standalone narrow/change/clear for one filament requirement, reachable from the part detail page
/// at any time after confirm (FR-012, US4) — addressed by the requirement's own real `Id`, already
/// returned by <see cref="GetPartEndpoint"/> (contracts/profiles-api.md). Uses `PATCH`, not `PUT`,
/// for the same reason `EditPrinterEndpoint` does: every request here is a partial modification by
/// construction.
/// </summary>
[Permission("parts:narrow-filament-requirement")]
public sealed class UpdatePartFilamentRequirementNarrowingEndpoint(UpdatePartFilamentRequirementNarrowingHandler handler)
    : Endpoint<UpdatePartFilamentRequirementNarrowingRequest, PartFilamentRequirementResponse>
{
    public override void Configure()
    {
        Patch("/api/v1/parts/{id}/filament-requirements/{requirementId}");
        Policies("Permission");
    }

    public override async Task HandleAsync(UpdatePartFilamentRequirementNarrowingRequest req, CancellationToken ct)
    {
        var partId = Route<Guid>("id");
        var requirementId = Route<Guid>("requirementId");

        var sub = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var (actorId, actorType) = Guid.TryParse(sub, out var parsedActorId)
            ? (parsedActorId, ActorType.Operator)
            : ((Guid?)null, ActorType.System);

        var command = new UpdatePartFilamentRequirementNarrowingCommand(
            partId, requirementId, req.NarrowFilamentVariantId, req.NarrowFilamentProfileId, actorId, actorType);
        var requirement = await handler.HandleAsync(command, ct);

        Response = new PartFilamentRequirementResponse
        {
            Id = requirement.Id,
            MaterialType = requirement.MaterialType,
            DetectedColorHex = requirement.DetectedColorHex,
            DetectedSubBrand = requirement.DetectedSubBrand,
            NarrowFilamentVariantId = requirement.NarrowFilamentVariantId,
            NarrowFilamentProfileId = requirement.NarrowFilamentProfileId,
        };
    }
}
