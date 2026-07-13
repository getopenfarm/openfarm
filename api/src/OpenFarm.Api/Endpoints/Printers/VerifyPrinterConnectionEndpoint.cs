using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class VerifyPrinterConnectionRequest
{
    public string IpAddress { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string AccessCode { get; set; } = string.Empty;
}

public sealed class VerifyPrinterConnectionRequestValidator : AbstractValidator<VerifyPrinterConnectionRequest>
{
    public VerifyPrinterConnectionRequestValidator()
    {
        RuleFor(x => x.IpAddress).NotEmpty();
        RuleFor(x => x.SerialNumber).NotEmpty();
        RuleFor(x => x.AccessCode).NotEmpty();
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
public sealed class VerifyPrinterConnectionEndpoint(IPrinterConnector connector)
    : Endpoint<VerifyPrinterConnectionRequest, VerifyPrinterConnectionResponse>
{
    public override void Configure()
    {
        Post("/api/v1/printers/verify-connection");
        Policies("Permission");
    }

    public override async Task HandleAsync(VerifyPrinterConnectionRequest req, CancellationToken ct)
    {
        var details = new PrinterConnectionDetails(req.IpAddress, req.SerialNumber, req.AccessCode);
        var verified = await connector.VerifyAsync(details, ct);

        if (!verified)
        {
            throw new PrinterConnectionVerificationFailedException(req.IpAddress);
        }

        Response = new VerifyPrinterConnectionResponse { Verified = true };
    }
}
