using System.Net;
using FluentValidation;

namespace OpenFarm.Application.Printers;

public sealed class EditPrinterConnectionCommandValidator : AbstractValidator<EditPrinterConnectionCommand>
{
    public EditPrinterConnectionCommandValidator()
    {
        RuleFor(x => x.PrinterId).NotEmpty();
        RuleFor(x => x.IpAddress).NotEmpty();
        RuleFor(x => x.IpAddress)
            .Must(ip => IPAddress.TryParse(ip, out _))
            .WithMessage("IpAddress must be a valid IPv4 or IPv6 address.")
            .When(x => !string.IsNullOrWhiteSpace(x.IpAddress));
        RuleFor(x => x.AccessCode).NotEmpty();
    }
}
