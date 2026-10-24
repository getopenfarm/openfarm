using System.Net;
using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class VerifyPrinterConnectionRequest
{
    public string Brand { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string AccessCode { get; set; } = string.Empty;
}

public sealed class VerifyPrinterConnectionRequestValidator : AbstractValidator<VerifyPrinterConnectionRequest>
{
    public VerifyPrinterConnectionRequestValidator()
    {
        RuleFor(x => x.Brand).NotEmpty();
        RuleFor(x => x.ModelCode).NotEmpty();
        RuleFor(x => x.IpAddress).NotEmpty();
        RuleFor(x => x.IpAddress)
            .Must(ip => IPAddress.TryParse(ip, out _))
            .WithMessage("IpAddress must be a valid IPv4 or IPv6 address.")
            .When(x => !string.IsNullOrWhiteSpace(x.IpAddress));
        // A Snapmaker reports its own serial and its API key is optional; the connector's verify is the authority.
        RuleFor(x => x.SerialNumber).NotEmpty().When(x => PrinterModelCatalog.UsesAccessCode(x.Brand));
        RuleFor(x => x.AccessCode).NotEmpty().When(x => PrinterModelCatalog.UsesAccessCode(x.Brand));
        RuleFor(x => x.AccessCode).NotNull().Unless(x => PrinterModelCatalog.UsesAccessCode(x.Brand));
    }
}

public sealed class VerifyPrinterConnectionResponse
{
    public bool Verified { get; set; }
}

/// <summary>
/// Pre-flight check the add/edit form calls before enabling submit (FR-001a/FR-002). Never persists.
/// </summary>
[Permission("printers:verify-connection")]
public sealed class VerifyPrinterConnectionEndpoint(IPrinterConnectorFactory connectorFactory)
    : Endpoint<VerifyPrinterConnectionRequest, VerifyPrinterConnectionResponse>
{
    public override void Configure()
    {
        Post("/api/v1/printers/verify-connection");
        Policies("Permission");
    }

    public override async Task HandleAsync(VerifyPrinterConnectionRequest req, CancellationToken ct)
    {
        var details = new PrinterConnectionDetails(req.Brand, req.ModelCode, req.IpAddress, req.SerialNumber, req.AccessCode);
        var verified = await connectorFactory.CreateConnector(details).VerifyAsync(details, ct);

        if (!verified)
        {
            throw new PrinterConnectionVerificationFailedException(req.IpAddress);
        }

        Response = new VerifyPrinterConnectionResponse { Verified = true };
    }
}
