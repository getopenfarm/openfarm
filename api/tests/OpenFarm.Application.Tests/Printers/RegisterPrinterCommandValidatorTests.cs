using FluentAssertions;
using OpenFarm.Application.Printers;

namespace OpenFarm.Application.Tests.Printers;

public class RegisterPrinterCommandValidatorTests
{
    private readonly RegisterPrinterCommandValidator _validator = new();

    private static RegisterPrinterCommand ValidCommand() => new(
        Name: "Printer 1",
        Brand: "Bambu",
        ModelCode: "P1S",
        IpAddress: "192.168.1.50",
        SerialNumber: "01P00A123456789",
        AccessCode: "12345678");

    [Fact]
    public void Validate_WithValidCommand_IsValid()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyName_IsInvalid(string name)
    {
        var result = _validator.Validate(ValidCommand() with { Name = name });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterPrinterCommand.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyIpAddress_IsInvalid(string ipAddress)
    {
        var result = _validator.Validate(ValidCommand() with { IpAddress = ipAddress });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterPrinterCommand.IpAddress));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptySerialNumber_IsInvalid(string serialNumber)
    {
        var result = _validator.Validate(ValidCommand() with { SerialNumber = serialNumber });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterPrinterCommand.SerialNumber));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyAccessCode_IsInvalid(string accessCode)
    {
        var result = _validator.Validate(ValidCommand() with { AccessCode = accessCode });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterPrinterCommand.AccessCode));
    }

    [Fact]
    public void Validate_SnapmakerWithoutSerialOrApiKey_IsValid()
    {
        // The U1 reports its own serial and its Moonraker API key is optional.
        var result = _validator.Validate(ValidCommand() with { Brand = "Snapmaker", ModelCode = "U1", SerialNumber = "", AccessCode = "" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithUnknownModel_IsInvalid()
    {
        var result = _validator.Validate(ValidCommand() with { ModelCode = "NotAModel" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("NotAModel"));
    }
}
