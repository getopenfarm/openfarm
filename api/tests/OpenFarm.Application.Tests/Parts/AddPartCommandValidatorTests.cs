using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Tests.Parts;

// TDD (research.md §12) — the only submit-time gate that's a pure FluentValidation rule (no
// filamentId/name/tags fields exist to validate at submit, per data-model.md's Validation rules).
public class AddPartCommandValidatorTests
{
    private static readonly AddPartCommandValidator Validator = new();

    [Theory]
    [InlineData("bracket.stl")]
    [InlineData("bracket.3mf")]
    [InlineData("bracket.gcode")]
    [InlineData("bracket.gco")]
    [InlineData("bracket.g")]
    [InlineData("BRACKET.STL")]
    public void Validate_RecognizedExtension_Passes(string fileName)
    {
        var result = Validator.Validate(new AddPartCommand(new MemoryStream(), fileName, null, null, null));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("bracket.obj")]
    [InlineData("bracket")]
    [InlineData("")]
    public void Validate_UnrecognizedExtension_Fails(string fileName)
    {
        var result = Validator.Validate(new AddPartCommand(new MemoryStream(), fileName, null, null, null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_BlankSlicerVersionOverride_Fails()
    {
        var result = Validator.Validate(new AddPartCommand(new MemoryStream(), "bracket.stl", SlicerEngine.OrcaSlicer, "   ", null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SlicerVersionOverrideWithoutEngine_Fails()
    {
        var result = Validator.Validate(new AddPartCommand(new MemoryStream(), "bracket.stl", null, "2.2.0", null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SlicerEngineAndVersionOverrideBothPresent_Passes()
    {
        var result = Validator.Validate(new AddPartCommand(new MemoryStream(), "bracket.stl", SlicerEngine.OrcaSlicer, "2.2.0", null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SlicerEngineOverrideOnlyNoVersion_Passes()
    {
        var result = Validator.Validate(new AddPartCommand(new MemoryStream(), "bracket.stl", SlicerEngine.OrcaSlicer, null, null));

        result.IsValid.Should().BeTrue();
    }
}
