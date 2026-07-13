using FluentValidation;

namespace OpenFarm.Application.Printers;

public sealed class EditPrinterConnectionCommandValidator : AbstractValidator<EditPrinterConnectionCommand>
{
    public EditPrinterConnectionCommandValidator()
    {
        RuleFor(x => x.PrinterId).NotEmpty();
        RuleFor(x => x.IpAddress).NotEmpty();
        RuleFor(x => x.AccessCode).NotEmpty();
    }
}
