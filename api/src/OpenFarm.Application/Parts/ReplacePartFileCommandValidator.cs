using FluentValidation;

namespace OpenFarm.Application.Parts;

public sealed class ReplacePartFileCommandValidator : AbstractValidator<ReplacePartFileCommand>
{
    private static readonly string[] RecognizedExtensions = [".stl", ".3mf", ".gcode", ".gco", ".g"];

    public ReplacePartFileCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .Must(fileName => RecognizedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
            .WithMessage("File must be a .stl, .3mf, or .gcode file.");
    }
}
