using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Profiles;
using UpdateFilamentProfileRawDataHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Profiles.UpdateFilamentProfileRawDataCommand, OpenFarm.Application.Profiles.FilamentProfileDetail>;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class UpdateFilamentProfileRawDataRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string RawDataJson { get; set; } = string.Empty;

    public string EntityType => "FilamentProfile";

    public string EventType => "FilamentProfileRawDataEdited";
}

public sealed class UpdateFilamentProfileRawDataRequestValidator : AbstractValidator<UpdateFilamentProfileRawDataRequest>
{
    public UpdateFilamentProfileRawDataRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RawDataJson).NotEmpty();
    }
}

// PUT /api/v1/profiles/filament/{id}/raw-data (FR-012).
[Permission("profiles:edit-filament-raw-data")]
public sealed class UpdateFilamentProfileRawDataEndpoint(UpdateFilamentProfileRawDataHandler handler)
    : Endpoint<UpdateFilamentProfileRawDataRequest, FilamentProfileDetailResponse>
{
    public override void Configure()
    {
        Put("/api/v1/profiles/filament/{id}/raw-data");
        Policies("Permission");
    }

    public override async Task HandleAsync(UpdateFilamentProfileRawDataRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var command = new UpdateFilamentProfileRawDataCommand(req.Id, req.RawDataJson);
        var detail = await handler.HandleAsync(command, ct);

        Response = FilamentProfileDetailResponse.FromDetail(detail);
    }
}
