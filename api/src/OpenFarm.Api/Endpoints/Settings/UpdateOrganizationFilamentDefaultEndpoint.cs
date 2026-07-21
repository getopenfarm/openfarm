using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;
using UpdateOrganizationFilamentDefaultHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Parts.UpdateOrganizationFilamentDefaultCommand, OpenFarm.Application.Parts.OrganizationFilamentDefaultResult>;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class UpdateOrganizationFilamentDefaultRequest : IActivityLoggable
{
    public string MaterialType { get; set; } = string.Empty;

    public Guid FilamentProfileId { get; set; }

    public string EntityType => "Organization";

    public string EventType => "FilamentDefaultUpdated";
}

public sealed class UpdateOrganizationFilamentDefaultRequestValidator : AbstractValidator<UpdateOrganizationFilamentDefaultRequest>
{
    public UpdateOrganizationFilamentDefaultRequestValidator()
    {
        RuleFor(x => x.FilamentProfileId).NotEmpty();
    }
}

public sealed class OrganizationFilamentDefaultResponse
{
    // Reflected by ActivityLogPostProcessor's default single-entity path — the well-known singleton
    // Organization this setting always belongs to.
    public Guid Id { get; set; } = Organization.WellKnownId;

    public string MaterialType { get; set; } = string.Empty;

    public Guid FilamentProfileId { get; set; }

    public static OrganizationFilamentDefaultResponse FromResult(OrganizationFilamentDefaultResult result) => new()
    {
        MaterialType = result.MaterialType,
        FilamentProfileId = result.FilamentProfileId,
    };
}

// PUT /api/v1/settings/filament-defaults/{materialType} — sets the org-wide default FilamentProfile
// for a resolved material type. Filament compatibility isn't enforced by Orca/Bambu Studio at slice
// time (unlike machine/process pairing), so this is safely independent of the default machine.
[Permission("settings:filament-defaults:update")]
public sealed class UpdateOrganizationFilamentDefaultEndpoint(UpdateOrganizationFilamentDefaultHandler handler)
    : Endpoint<UpdateOrganizationFilamentDefaultRequest, OrganizationFilamentDefaultResponse>
{
    public override void Configure()
    {
        Put("/api/v1/settings/filament-defaults/{materialType}");
        Policies("Permission");
    }

    public override async Task HandleAsync(UpdateOrganizationFilamentDefaultRequest req, CancellationToken ct)
    {
        req.MaterialType = Route<string>("materialType") ?? string.Empty;

        var result = await handler.HandleAsync(new UpdateOrganizationFilamentDefaultCommand(req.MaterialType, req.FilamentProfileId), ct);
        Response = OrganizationFilamentDefaultResponse.FromResult(result);
    }
}
