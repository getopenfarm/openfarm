using FluentAssertions;
using OpenFarm.Application.Auth;

namespace OpenFarm.Application.Tests.Auth;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_IsValid()
    {
        var result = _validator.Validate(new LoginCommand("admin@openfarm.local", "ChangeMe123!"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyUsername_IsInvalid(string username)
    {
        var result = _validator.Validate(new LoginCommand(username, "ChangeMe123!"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginCommand.Username));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyPassword_IsInvalid(string password)
    {
        var result = _validator.Validate(new LoginCommand("admin@openfarm.local", password));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginCommand.Password));
    }
}
