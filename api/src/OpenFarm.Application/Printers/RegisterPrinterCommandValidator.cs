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
        RuleFor(x => x.SerialNumber).NotEmpty();
        RuleFor(x => x.AccessCode).NotEmpty();

        RuleFor(x => x)
            .Must(x => PrinterModelCatalog.TryResolve(x.Brand, x.ModelCode, out _))
            .WithMessage(x => $"Unknown printer model '{x.ModelCode}' for brand '{x.Brand}'.")
            .When(x => !string.IsNullOrWhiteSpace(x.Brand) && !string.IsNullOrWhiteSpace(x.ModelCode));
    }
}
