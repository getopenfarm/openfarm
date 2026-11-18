using FluentAssertions;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public class OidcSettingsTests
{
    private static AuthenticationSettingsValues Enabled => new()
    {
        OidcEnabled = true,
        Authority = "https://idp.example/application/o/farm/",
        ClientId = "farm",
        ClientSecret = "credential",
    };

    [Fact]
    public void Disabled_DefaultsPreservePasswordLogin()
    {
        var settings = new OidcSettings(new());
        settings.Enabled.Should().BeFalse();
        settings.AllowsPassword(Guid.NewGuid()).Should().BeTrue();
        settings.AllowsBrowserSession(Guid.NewGuid(), null).Should().BeTrue();
        settings.AllowsBrowserSession(Guid.NewGuid(), "external").Should().BeFalse();
    }

    [Theory]
    [InlineData("http://idp.example/application/o/farm/")]
    [InlineData("https://user:password@idp.example/")]
    [InlineData("https://idp.example/?x=1")]
    [InlineData("https://idp.example/#fragment")]
    [InlineData("not-an-authority")]
    public void Enabled_RejectsInvalidAuthorities(string authority) =>
        ((Action)(() => _ = new OidcSettings(Enabled with { Authority = authority }))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void InternalDisabled_OnlyExplicitRecoveryUsersCanUsePasswordsOrOldPasswordSessions()
    {
        var recovery = Guid.NewGuid();
        var settings = new OidcSettings(Enabled with { InternalAuthenticationEnabled = false, RecoveryUserIds = [recovery] });
        settings.AllowsPassword(recovery).Should().BeTrue();
        settings.AllowsBrowserSession(recovery, null).Should().BeTrue();
        settings.AllowsPassword(Guid.NewGuid()).Should().BeFalse();
        settings.AllowsBrowserSession(Guid.NewGuid(), null).Should().BeFalse();
        settings.AllowsBrowserSession(Guid.NewGuid(), settings.SessionIdentity).Should().BeTrue();
    }

    [Fact]
    public void ChangingClient_PreservesIdentityLinksButInvalidatesExternalSessions()
    {
        var old = new OidcSettings(Enabled);
        var changed = new OidcSettings(Enabled with { ClientId = "replacement" });
        changed.ProviderKey.Should().Be(old.ProviderKey);
        changed.AllowsBrowserSession(Guid.NewGuid(), old.SessionIdentity).Should().BeFalse();
    }

    [Fact]
    public void InternalDisabled_RejectsMissingProviderOrRecoveryAccount()
    {
        ((Action)(() => _ = new OidcSettings(new() { InternalAuthenticationEnabled = false }))).Should().Throw<InvalidOperationException>();
        ((Action)(() => _ = new OidcSettings(Enabled with { InternalAuthenticationEnabled = false }))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void RejectsUnboundedExternalSessionLifetimes(int hours) =>
        ((Action)(() => _ = new OidcSettings(Enabled with { SessionHours = hours }))).Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData(null)]
    [InlineData("http://farm.example")]
    public void Enabled_RequiresASecurePublicUrl(string? value) =>
        ((Action)(() => new OidcSettings(Enabled).ValidatePublicUrl(value))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void ProviderTest_IsBoundToCredentialsClientIssuerAndCallbackOrigin()
    {
        const string origin = "https://farm.example";
        var settings = new OidcSettings(Enabled);
        var identity = settings.TestIdentity(origin);
        settings.TestIdentity("https://other.example").Should().NotBe(identity);
        new OidcSettings(Enabled with { ClientSecret = "rotated" }).TestIdentity(origin).Should().NotBe(identity);
        new OidcSettings(Enabled with { ClientId = "replacement" }).TestIdentity(origin).Should().NotBe(identity);
        new OidcSettings(Enabled with { Authority = "https://other.example/" }).TestIdentity(origin).Should().NotBe(identity);
        new OidcSettings(Enabled with { DisplayName = "New name", JitEnabled = true }).TestIdentity(origin).Should().Be(identity);
    }
}
