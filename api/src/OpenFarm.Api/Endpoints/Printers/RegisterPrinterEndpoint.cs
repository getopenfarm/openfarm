using System.Net;
using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using RegisterPrinterHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.RegisterPrinterCommand, OpenFarm.Domain.Printers.Printer>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class RegisterPrinterRequest : IActivityLoggable
{
    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string AccessCode { get; set; } = string.Empty;

    public string EntityType => "Printer";

    public string EventType => "PrinterRegistered";
}

public sealed class RegisterPrinterRequestValidator : AbstractValidator<RegisterPrinterRequest>
{
    public RegisterPrinterRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Brand).NotEmpty();
        RuleFor(x => x.ModelCode).NotEmpty();
        RuleFor(x => x.IpAddress).NotEmpty();
        RuleFor(x => x.IpAddress)
            .Must(ip => IPAddress.TryParse(ip, out _))
            .WithMessage("IpAddress must be a valid IPv4 or IPv6 address.")
            .When(x => !string.IsNullOrWhiteSpace(x.IpAddress));
        RuleFor(x => x.SerialNumber).NotEmpty();
        RuleFor(x => x.AccessCode).NotEmpty();
    }
}

/// <summary>
/// Register a new printer per <c>contracts/printers-api.md</c>. Re-runs verify-connection
/// server-side (FR-001a); rejects duplicate serial numbers (409, FR-001b) and unresolvable
/// (brand, modelCode) combinations (422) via <see cref="Api.ExceptionHandling.DomainExceptionHandler"/>.
/// </summary>
[Permission("printers:register")]
public sealed class RegisterPrinterEndpoint(RegisterPrinterHandler handler) : Endpoint<RegisterPrinterRequest, PrinterSummaryResponse>
{
    public override void Configure()
    {
        Post("/api/v1/printers");
        Policies("Permission");
    }

    public override async Task HandleAsync(RegisterPrinterRequest req, CancellationToken ct)
    {
        var command = new RegisterPrinterCommand(req.Name, req.Brand, req.ModelCode, req.IpAddress, req.SerialNumber, req.AccessCode);
        var printer = await handler.HandleAsync(command, ct);

        Response = PrinterSummaryResponse.FromDomain(printer);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
