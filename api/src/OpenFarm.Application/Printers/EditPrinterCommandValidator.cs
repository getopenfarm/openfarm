using System.Net;
using FluentValidation;

namespace OpenFarm.Application.Printers;

public sealed class EditPrinterCommandValidator : AbstractValidator<EditPrinterCommand>
{
    public EditPrinterCommandValidator()
    {
        RuleFor(x => x.PrinterId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();

        RuleFor(x => x.AccessCode)
            .NotEmpty()
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
    }
}
