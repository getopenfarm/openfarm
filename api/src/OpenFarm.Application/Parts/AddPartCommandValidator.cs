using FluentValidation;

namespace OpenFarm.Application.Parts;

public sealed class AddPartCommandValidator : AbstractValidator<AddPartCommand>
{
    // Mirrors IPartFileClassifier's own recognized extensions (research.md §1) — a cheap pre-flight
    // gate that doesn't need to open the file, ahead of the classifier's own zip-entry inspection.
    private static readonly string[] RecognizedExtensions = [".3mf", ".gcode", ".gco", ".g"];

    public AddPartCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .Must(fileName => RecognizedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
            .WithMessage("File must be a project 3MF, a sliced 3MF, or a .gcode file. STL carries no slicing settings.");

        RuleFor(x => x.SlicerVersionOverride)
            .Must(version => version is null || !string.IsNullOrWhiteSpace(version))
            .WithMessage("SlicerVersion must not be blank when provided.");

        RuleFor(x => x)
            .Must(x => x.SlicerVersionOverride is null || x.SlicerEngineOverride is not null)
            .WithMessage("SlicerVersion requires SlicerEngine to also be specified.");
    }
}
