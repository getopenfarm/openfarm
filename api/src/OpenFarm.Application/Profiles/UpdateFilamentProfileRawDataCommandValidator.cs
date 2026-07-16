using FluentValidation;

namespace OpenFarm.Application.Profiles;

public sealed class UpdateFilamentProfileRawDataCommandValidator : AbstractValidator<UpdateFilamentProfileRawDataCommand>
{
    public UpdateFilamentProfileRawDataCommandValidator()
    {
        RuleFor(x => x.FilamentProfileId).NotEmpty();
        RuleFor(x => x.RawDataJson).NotEmpty();
    }
}
