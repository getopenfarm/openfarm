using FluentValidation;

namespace OpenFarm.Application.Profiles;

public sealed class UpdateMachineProfileRawDataCommandValidator : AbstractValidator<UpdateMachineProfileRawDataCommand>
{
    public UpdateMachineProfileRawDataCommandValidator()
    {
        RuleFor(x => x.MachineProfileId).NotEmpty();
        RuleFor(x => x.RawDataJson).NotEmpty();
    }
}
