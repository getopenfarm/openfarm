using FluentAssertions;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class RegisterSpoolCommandValidatorTests
{
    private readonly RegisterSpoolCommandValidator _validator = new();

    private static RegisterSpoolCommand ValidCommand() => new(
        FilamentVariantId: Guid.NewGuid(),
        Label: "Shelf A-1",
        SpoolWeightGrams: 1000,
        RemainingWeightGrams: 1000);

    [Fact]
    public void Validate_WithValidCommand_IsValid()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyFilamentVariantId_IsInvalid()
    {
        var result = _validator.Validate(ValidCommand() with { FilamentVariantId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterSpoolCommand.FilamentVariantId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Validate_WithZeroOrNegativeSpoolWeight_IsInvalid(decimal spoolWeightGrams)
    {
        var result = _validator.Validate(ValidCommand() with { SpoolWeightGrams = spoolWeightGrams });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterSpoolCommand.SpoolWeightGrams));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Validate_WithZeroOrNegativeRemainingWeight_IsInvalid(decimal remainingWeightGrams)
    {
        var result = _validator.Validate(ValidCommand() with { RemainingWeightGrams = remainingWeightGrams });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterSpoolCommand.RemainingWeightGrams));
    }

    [Fact]
    public void Validate_WithRemainingWeightGreaterThanSpoolWeight_IsInvalid()
    {
        var result = _validator.Validate(ValidCommand() with { SpoolWeightGrams = 500, RemainingWeightGrams = 600 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterSpoolCommand.RemainingWeightGrams));
    }

    [Fact]
    public void Validate_WithPartiallyUsedSpool_RemainingLessThanSpoolWeight_IsValid()
    {
        var result = _validator.Validate(ValidCommand() with { SpoolWeightGrams = 1000, RemainingWeightGrams = 400 });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithNullRemainingWeight_IsValid()
    {
        var result = _validator.Validate(ValidCommand() with { RemainingWeightGrams = null });

        result.IsValid.Should().BeTrue();
    }
}
