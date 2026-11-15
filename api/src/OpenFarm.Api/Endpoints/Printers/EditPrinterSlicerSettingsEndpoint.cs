using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;
using EditPrinterSlicerSettingsHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.EditPrinterSlicerSettingsCommand, OpenFarm.Domain.Printers.Printer>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class EditPrinterSlicerSettingsRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    // String enum on the wire. Applied exactly as sent (null = clear): the caller sends the full settings.
    public string? BuildPlateType { get; set; }

    public string EntityType => "Printer";

    public string EventType => "PrinterSlicerSettingsEdited";
}

public sealed class EditPrinterSlicerSettingsRequestValidator : AbstractValidator<EditPrinterSlicerSettingsRequest>
{
    public EditPrinterSlicerSettingsRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.BuildPlateType)
            .Must(v => Enum.TryParse<BuildPlateType>(v, ignoreCase: true, out _))
            .WithMessage("BuildPlateType must be one of: Cool, Engineering, HighTemp, TexturedPei, TexturedCool, Supertack.")
            .When(x => x.BuildPlateType is not null);
    }
}

/// <summary>
/// Slicer settings of one printer (today: the build plate). <c>PUT</c> because the body is the whole settings
/// document. Never blocked by the printer's current status: operators swap plates while it prints.
/// </summary>
[Permission("printers:edit-slicer-settings")]
public sealed class EditPrinterSlicerSettingsEndpoint(EditPrinterSlicerSettingsHandler handler)
    : Endpoint<EditPrinterSlicerSettingsRequest, PrinterSummaryResponse>
{
    public override void Configure()
    {
        Put("/api/v1/printers/{id}/slicer-settings");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditPrinterSlicerSettingsRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        // Same JWT-claim resolution ActivityLogPostProcessor uses for the base event, so the granular
        // plate event the handler writes attributes the same actor.
        var sub = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var (actorId, actorType) = Guid.TryParse(sub, out var parsedActorId)
            ? (parsedActorId, ActorType.Operator)
            : ((Guid?)null, ActorType.System);

        var buildPlateType = req.BuildPlateType is not null
            ? Enum.Parse<BuildPlateType>(req.BuildPlateType, ignoreCase: true)
            : (BuildPlateType?)null;

        var printer = await handler.HandleAsync(
            new EditPrinterSlicerSettingsCommand(req.Id, buildPlateType, actorId, actorType), ct);

        Response = PrinterSummaryResponse.FromDomain(printer);
    }
}
