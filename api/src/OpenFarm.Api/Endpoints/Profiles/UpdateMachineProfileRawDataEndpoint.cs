using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Profiles;
using UpdateMachineProfileRawDataHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Profiles.UpdateMachineProfileRawDataCommand, OpenFarm.Application.Profiles.MachineProfileDetail>;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class UpdateMachineProfileRawDataRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string RawDataJson { get; set; } = string.Empty;

    public string EntityType => "MachineProfile";

    public string EventType => "MachineProfileRawDataEdited";
}

public sealed class UpdateMachineProfileRawDataRequestValidator : AbstractValidator<UpdateMachineProfileRawDataRequest>
{
    public UpdateMachineProfileRawDataRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RawDataJson).NotEmpty();
    }
}

// PUT /api/v1/profiles/machine/{id}/raw-data (FR-012). Returns the same shape as the detail GET,
// with derived fields recomputed from the saved value — no separate re-derivation step to keep in
// sync (data-model.md).
[Permission("profiles:edit-machine-raw-data")]
public sealed class UpdateMachineProfileRawDataEndpoint(UpdateMachineProfileRawDataHandler handler)
    : Endpoint<UpdateMachineProfileRawDataRequest, MachineProfileDetailResponse>
{
    public override void Configure()
    {
        Put("/api/v1/profiles/machine/{id}/raw-data");
        Policies("Permission");
    }

    public override async Task HandleAsync(UpdateMachineProfileRawDataRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var command = new UpdateMachineProfileRawDataCommand(req.Id, req.RawDataJson);
        var detail = await handler.HandleAsync(command, ct);

        Response = MachineProfileDetailResponse.FromDetail(detail);
    }
}
