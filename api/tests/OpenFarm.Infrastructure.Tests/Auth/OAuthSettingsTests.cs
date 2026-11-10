using FluentAssertions;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public sealed class OAuthSettingsTests
{
    [Theory]
    [InlineData(null, "https://farm.example")]
    [InlineData("", null)]
    [InlineData("false", "https://farm.example")]
    [InlineData("False", null)]
    public void OffByDefault(string? enabled, string? origin)
    {
        var settings = new OAuthSettings(enabled, origin);
        settings.Enabled.Should().BeFalse();
        settings.Issuer.Should().BeNull();
        settings.Resource.Should().BeNull();
    }

    [Theory]
    [InlineData("https://farm.example")]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:6868")]
    [InlineData("http://[::1]:6868")]
    public void OnNamesTheOriginOnly(string origin)
    {
        var settings = new OAuthSettings(" TRUE ", origin);
        settings.Enabled.Should().BeTrue();
        settings.Issuer.Should().Be(origin);
        settings.Resource.Should().Be(origin + "/mcp");
        settings.ResourceMetadataUrl.Should().Be(origin + "/.well-known/oauth-protected-resource/mcp");
    }

    [Fact]
    public void StartupFailsWithAClearMessage()
    {
        FluentActions.Invoking(() => new OAuthSettings("true", null)).Should().Throw<InvalidOperationException>()
            .WithMessage("OAUTH_ENABLED requires ORIGIN (for example https://farm.example.com).");
        FluentActions.Invoking(() => new OAuthSettings("true", "http://farm.lan")).Should().Throw<InvalidOperationException>()
            .WithMessage("OAUTH_ENABLED requires an HTTPS ORIGIN. HTTP is allowed only for localhost.");
        FluentActions.Invoking(() => new OAuthSettings("yes", "https://farm.example")).Should().Throw<InvalidOperationException>()
            .WithMessage("OAUTH_ENABLED must be true or false.");
    }
}
