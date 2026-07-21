using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Profiles;
using SetMachineProfileDefaultProcessHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Profiles.SetMachineProfileDefaultProcessCommand, OpenFarm.Application.Profiles.MachineProfileDefaultProcessResult>;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class SetMachineProfileDefaultProcessRequest : IActivityLoggable
{
    public Guid MachineProfileId { get; set; }

    public Guid ProcessProfileId { get; set; }

    public string EntityType => "MachineProfile";

    public string EventType => "MachineProfileDefaultProcessSet";
}

public sealed class SetMachineProfileDefaultProcessRequestValidator : AbstractValidator<SetMachineProfileDefaultProcessRequest>
{
    public SetMachineProfileDefaultProcessRequestValidator()
    {
        RuleFor(x => x.ProcessProfileId).NotEmpty();
    }
}

public sealed class MachineProfileDefaultProcessResponse
{
    public Guid Id { get; set; }

    public Guid ProcessProfileId { get; set; }

    public static MachineProfileDefaultProcessResponse FromResult(MachineProfileDefaultProcessResult result) => new()
    {
        Id = result.MachineProfileId,
        ProcessProfileId = result.ProcessProfileId,
    };
}

// PUT /api/v1/profiles/machine/{id}/default-process-profile — pairs a ProcessProfile to a specific
// MachineProfile. A process preset is only ever valid for the exact machine it was authored/shipped
// for (research.md), so this is always an explicit operator action, never an auto-inferred default.
[Permission("profiles:machine:set-default-process")]
public sealed class SetMachineProfileDefaultProcessEndpoint(SetMachineProfileDefaultProcessHandler handler)
    : Endpoint<SetMachineProfileDefaultProcessRequest, MachineProfileDefaultProcessResponse>
{
    public override void Configure()
    {
        Put("/api/v1/profiles/machine/{id}/default-process-profile");
        Policies("Permission");
    }

    public override async Task HandleAsync(SetMachineProfileDefaultProcessRequest req, CancellationToken ct)
    {
        req.MachineProfileId = Route<Guid>("id");

        var result = await handler.HandleAsync(new SetMachineProfileDefaultProcessCommand(req.MachineProfileId, req.ProcessProfileId), ct);
        Response = MachineProfileDefaultProcessResponse.FromResult(result);
    }
}
