using FluentAssertions;
using OpenFarm.Application.ConnectionSecrets;

namespace OpenFarm.Application.Tests.ConnectionSecrets;

public class CreateConnectionSecretCommandValidatorTests
{
    private readonly CreateConnectionSecretCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_IsValid()
    {
        var result = _validator.Validate(new CreateConnectionSecretCommand("AC12345", "real-secret-value"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyPrinterSerialNumber_IsInvalid(string serialNumber)
    {
        var result = _validator.Validate(new CreateConnectionSecretCommand(serialNumber, "real-secret-value"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateConnectionSecretCommand.PrinterSerialNumber));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyPlainAccessCode_IsInvalid(string plainAccessCode)
    {
        var result = _validator.Validate(new CreateConnectionSecretCommand("AC12345", plainAccessCode));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateConnectionSecretCommand.PlainAccessCode));
    }
}
