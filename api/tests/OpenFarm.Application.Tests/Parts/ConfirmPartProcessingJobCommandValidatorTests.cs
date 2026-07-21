using FluentAssertions;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.Tests.Parts;

// TDD (research.md §12) — the one Confirm rule that's stateless (no job/DB context needed): at most
// one narrow-override field per entry. Kind-aware (Add-requires-name / Replace-rejects-name-tags) and
// requirementIndex-match rules need the job's own Kind/PreviewFilamentRequirements, so they're
// enforced in ConfirmPartProcessingJobCommandHandler instead (test-after, orchestration-adjacent).
public class ConfirmPartProcessingJobCommandValidatorTests
{
    private static readonly ConfirmPartProcessingJobCommandValidator Validator = new();

    [Fact]
    public void Validate_OverrideWithOnlyVariantId_Passes()
    {
        var command = new ConfirmPartProcessingJobCommand(
            Guid.NewGuid(), "Widget", null, [new FilamentRequirementOverride(0, Guid.NewGuid(), null)], null);

        Validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_OverrideWithBothVariantAndProfileId_Fails()
    {
        var command = new ConfirmPartProcessingJobCommand(
            Guid.NewGuid(), "Widget", null, [new FilamentRequirementOverride(0, Guid.NewGuid(), Guid.NewGuid())], null);

        Validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NoOverrides_Passes()
    {
        var command = new ConfirmPartProcessingJobCommand(Guid.NewGuid(), "Widget", null, null, null);

        Validator.Validate(command).IsValid.Should().BeTrue();
    }
}
