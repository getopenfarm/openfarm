using FluentAssertions;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public class ProvisioningSettingsTests
{
    [Fact]
    public void DefaultsKeepAutomaticProvisioningDisabled()
    {
        var settings = Build(new());
        settings.JitEnabled.Should().BeFalse();
        settings.RequireVerifiedEmail.Should().BeTrue();
        settings.DefaultGroup.Should().Be("Viewers");
    }

    [Fact]
    public void JitRequiresAnOidcProvider() =>
        ((Action)(() => Build(new() { JitEnabled = true }))).Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void JitRequiresADefaultGroup(string group) =>
        ((Action)(() => Build(new()
        {
            OidcEnabled = true,
            Authority = "https://identity.example/application/o/farm/",
            ClientId = "farm",
            ClientSecret = "oidc-client-credential",
            JitEnabled = true,
            DefaultGroup = group,
        }))).Should().Throw<InvalidOperationException>();

    private static ProvisioningSettings Build(AuthenticationSettingsValues value) => new(value, new OidcSettings(value));
}
