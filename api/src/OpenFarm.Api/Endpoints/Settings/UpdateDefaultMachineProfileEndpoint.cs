using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;
using UpdateDefaultMachineProfileHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Parts.UpdateDefaultMachineProfileCommand, OpenFarm.Application.Parts.DefaultMachineProfileResult>;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class UpdateDefaultMachineProfileRequest : IActivityLoggable
{
    public Guid MachineProfileId { get; set; }

    public string EntityType => "Organization";

    public string EventType => "DefaultMachineProfileUpdated";
}

public sealed class UpdateDefaultMachineProfileRequestValidator : AbstractValidator<UpdateDefaultMachineProfileRequest>
{
    public UpdateDefaultMachineProfileRequestValidator()
    {
        RuleFor(x => x.MachineProfileId).NotEmpty();
    }
}

public sealed class DefaultMachineProfileResponse
{
    // Reflected by ActivityLogPostProcessor's default single-entity path — the well-known singleton
    // Organization this setting always belongs to.
    public Guid Id { get; set; } = Organization.WellKnownId;

    public Guid MachineProfileId { get; set; }

    public string MachineProfileName { get; set; } = string.Empty;

    public Guid? DefaultProcessProfileId { get; set; }

    public static DefaultMachineProfileResponse FromResult(DefaultMachineProfileResult result) => new()
    {
        MachineProfileId = result.MachineProfileId,
        MachineProfileName = result.MachineProfileName,
        DefaultProcessProfileId = result.DefaultProcessProfileId,
    };
}

// PUT /api/v1/settings/default-machine-profile — sets the org-wide default MachineProfile used to
// resolve slicing (research.md — mirrors the existing slicing-defaults engine/version pattern).
[Permission("settings:default-machine-profile:update")]
public sealed class UpdateDefaultMachineProfileEndpoint(UpdateDefaultMachineProfileHandler handler)
    : Endpoint<UpdateDefaultMachineProfileRequest, DefaultMachineProfileResponse>
{
    public override void Configure()
    {
        Put("/api/v1/settings/default-machine-profile");
        Policies("Permission");
    }

    public override async Task HandleAsync(UpdateDefaultMachineProfileRequest req, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new UpdateDefaultMachineProfileCommand(req.MachineProfileId), ct);
        Response = DefaultMachineProfileResponse.FromResult(result);
    }
}
