using System.IdentityModel.Tokens.Jwt;
using System.Net;
using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;
using EditPrinterHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.EditPrinterCommand, OpenFarm.Domain.Printers.Printer>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class EditPrinterRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    public string? AccessCode { get; set; }

    public string? Brand { get; set; }

    public string? ModelCode { get; set; }

    public string? SerialNumber { get; set; }

    // 009 US2 — applied exactly as sent (null = clear), unlike IpAddress/AccessCode above
    // (contracts/profiles-api.md).
    public Guid? MachineProfileId { get; set; }

    public Guid? ProcessProfileId { get; set; }

    // 012 US1 — string enum on the wire (contracts/printers-api.md), applied exactly as sent (null =
    // clear); no machine-profile prerequisite, unlike ProcessProfileId above.
    public string? BuildPlateType { get; set; }

    public string EntityType => "Printer";

    public string EventType => "PrinterEdited";
}

public sealed class EditPrinterRequestValidator : AbstractValidator<EditPrinterRequest>
{
    public EditPrinterRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();

        // NotNull, not NotEmpty: a Snapmaker without an API key changes its IP with an empty one. A blank Bambu access
        // code just fails the verify that runs before anything is saved.
        RuleFor(x => x.AccessCode)
            .NotNull()
            .WithMessage("Provide both ipAddress and accessCode to change connection details, or neither to keep the current connection.")
            .When(x => !string.IsNullOrWhiteSpace(x.IpAddress));

        RuleFor(x => x.IpAddress)
            .NotEmpty()
            .WithMessage("Provide both ipAddress and accessCode to change connection details, or neither to keep the current connection.")
            .When(x => !string.IsNullOrWhiteSpace(x.AccessCode));

        RuleFor(x => x.IpAddress)
            .Must(ip => IPAddress.TryParse(ip, out _))
            .WithMessage("IpAddress must be a valid IPv4 or IPv6 address.")
            .When(x => !string.IsNullOrWhiteSpace(x.IpAddress));

        RuleFor(x => x.BuildPlateType)
            .Must(v => Enum.TryParse<BuildPlateType>(v, ignoreCase: true, out _))
            .WithMessage("BuildPlateType must be one of: Cool, Engineering, HighTemp, TexturedPei, TexturedCool, Supertack.")
            .When(x => x.BuildPlateType is not null);
    }
}

/// <summary>
/// Single edit operation covering a printer's name and, optionally, its connection details
/// (research.md §1) — replaces the connection-only <c>PUT .../connection</c> edit. Uses
/// <c>PATCH</c>, not <c>PUT</c>, because every request this endpoint accepts is a partial
/// modification by construction (research.md §1a). Never blocked by the printer's current status
/// (FR-001).
/// </summary>
[Permission("printers:edit-connection")]
public sealed class EditPrinterEndpoint(EditPrinterHandler handler)
    : Endpoint<EditPrinterRequest, PrinterSummaryResponse>
{
    public override void Configure()
    {
        Patch("/api/v1/printers/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditPrinterRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        // Same JWT-claim resolution ActivityLogPostProcessor uses for the base PrinterEdited event —
        // needed here too so the handler's own granular profile-link events (FR-016) attribute the
        // same actor, not a second, possibly-inconsistent extraction.
        var sub = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var (actorId, actorType) = Guid.TryParse(sub, out var parsedActorId)
            ? (parsedActorId, ActorType.Operator)
            : ((Guid?)null, ActorType.System);

        var buildPlateType = req.BuildPlateType is not null
            ? Enum.Parse<BuildPlateType>(req.BuildPlateType, ignoreCase: true)
            : (BuildPlateType?)null;

        var command = new EditPrinterCommand(
            req.Id, req.Name, req.IpAddress, req.AccessCode, req.Brand, req.ModelCode, req.SerialNumber,
            req.MachineProfileId, req.ProcessProfileId, buildPlateType, actorId, actorType);
        var printer = await handler.HandleAsync(command, ct);

        Response = PrinterSummaryResponse.FromDomain(printer);
    }
}
