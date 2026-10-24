using System.Net;
using FluentValidation;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed class RegisterPrinterCommandValidator : AbstractValidator<RegisterPrinterCommand>
{
    public RegisterPrinterCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
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

        RuleFor(x => x)
            .Must(x => PrinterModelCatalog.TryResolve(x.Brand, x.ModelCode, out _))
            .WithMessage(x => $"Unknown printer model '{x.ModelCode}' for brand '{x.Brand}'.")
            .When(x => !string.IsNullOrWhiteSpace(x.Brand) && !string.IsNullOrWhiteSpace(x.ModelCode));
    }
}
