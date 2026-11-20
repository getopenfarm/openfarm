using FluentAssertions;
using OpenFarm.Api.Security;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class OidcReturnToTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("https://attacker.example/")]
    [InlineData("//attacker.example/")]
    [InlineData("/\\attacker.example/")]
    [InlineData("/\n/attacker.example/")]
    [InlineData("/api/v1/auth/sso/login")]
    [InlineData("/auth/complete")]
    public void RejectsUnsafeDestinations(string? value) => OidcAuthentication.LocalReturnTo(value).Should().Be("/");

    [Theory]
    [InlineData("/printers")]
    [InlineData("/connect?request=example")]
    public void PreservesLocalDestinations(string value) => OidcAuthentication.LocalReturnTo(value).Should().Be(value);
}
