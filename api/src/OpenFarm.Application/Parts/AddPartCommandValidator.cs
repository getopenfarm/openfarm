using FluentValidation;

namespace OpenFarm.Application.Parts;

public sealed class AddPartCommandValidator : AbstractValidator<AddPartCommand>
{
    // Mirrors IPartFileClassifier's own recognized extensions (research.md §1) — a cheap pre-flight
    // gate that doesn't need to open the file, ahead of the classifier's own zip-entry inspection.
    private static readonly string[] RecognizedExtensions = [".stl", ".3mf", ".gcode", ".gco", ".g"];

    public AddPartCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .Must(fileName => RecognizedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
            .WithMessage("File must be a .stl, .3mf, or .gcode file.");
    }
}
