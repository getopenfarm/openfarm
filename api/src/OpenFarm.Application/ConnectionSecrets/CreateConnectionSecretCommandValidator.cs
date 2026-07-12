using FluentValidation;

namespace OpenFarm.Application.ConnectionSecrets;

public sealed class CreateConnectionSecretCommandValidator : AbstractValidator<CreateConnectionSecretCommand>
{
    public CreateConnectionSecretCommandValidator()
    {
        RuleFor(x => x.PrinterSerialNumber).NotEmpty();
        RuleFor(x => x.PlainAccessCode).NotEmpty();
    }
}
