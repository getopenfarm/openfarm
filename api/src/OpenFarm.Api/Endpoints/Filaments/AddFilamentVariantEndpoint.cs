using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using AddFilamentVariantHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.AddFilamentVariantCommand, OpenFarm.Domain.FilamentTracking.FilamentVariant>;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class AddFilamentVariantRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public string EntityType => "Filament";

    public string EventType => "FilamentVariantAdded";
}

public sealed class AddFilamentVariantRequestValidator : AbstractValidator<AddFilamentVariantRequest>
{
    public AddFilamentVariantRequestValidator()
    {
        RuleFor(x => x.ColorName).NotEmpty();
        RuleFor(x => x.ColorHex).NotEmpty();
    }
}

/// <summary>Adds a color variant to an existing Filament. 404 on an unresolvable FilamentId; 409 on a duplicate (FilamentId, ColorName).</summary>
[Permission("filaments:add-variant")]
public sealed class AddFilamentVariantEndpoint(AddFilamentVariantHandler handler)
    : Endpoint<AddFilamentVariantRequest, FilamentVariantResponse>
{
    public override void Configure()
    {
        Post("/api/v1/filaments/{id}/variants");
        Policies("Permission");
    }

    public override async Task HandleAsync(AddFilamentVariantRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var variant = await handler.HandleAsync(new AddFilamentVariantCommand(req.Id, req.ColorName, req.ColorHex), ct);

        Response = FilamentVariantResponse.FromDomain(variant);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
