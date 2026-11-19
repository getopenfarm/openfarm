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
    [InlineData("")]
    [InlineData("   ")]
    public void BlankProviderName_FallsBackToANeutralLabel(string name) =>
        new OidcSettings(Enabled with { DisplayName = name }).DisplayName.Should().Be("SSO");

    [Fact]
    public void ProviderName_IsOptionalAndTrimmed()
    {
        new AuthenticationSettingsValues().DisplayName.Should().BeEmpty();
        new OidcSettings(Enabled with { DisplayName = "  Corporate login " }).DisplayName.Should().Be("Corporate login");
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
    [InlineData(null)]
    [InlineData("http://farm.example")]
    public void Enabled_RequiresASecurePublicUrl(string? value) =>
        ((Action)(() => new OidcSettings(Enabled).ValidatePublicUrl(value))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void GroupsClaim_DefaultsToGroups_SoSettingsSavedBeforeTheFieldKeepWorking() =>
        new AuthenticationSettingsValues().GroupsClaim.Should().Be("groups");

    [Theory]
    [InlineData("groups")]
    [InlineData("entitlements")]
    [InlineData("roles")]
    [InlineData("https://farm.example/claims/roles")]
    public void Jit_AcceptsAnyProviderClaimName(string claim)
    {
        var values = Enabled with { JitEnabled = true, GroupsClaim = claim };
        new ProvisioningSettings(values, new OidcSettings(values)).GroupsClaim.Should().Be(claim);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("tab\tseparated")]
    [InlineData("sub")]
    [InlineData("Email")]
    [InlineData("NAME")]
    [InlineData("preferred_username")]
    public void Jit_RejectsABlankSpacedOrIdentityGroupsClaim(string claim)
    {
        var values = Enabled with { JitEnabled = true, GroupsClaim = claim };
        ((Action)(() => _ = new ProvisioningSettings(values, new OidcSettings(values)))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Jit_RejectsAGroupsClaimOver256Characters()
    {
        var values = Enabled with { JitEnabled = true, GroupsClaim = new string('a', 257) };
        ((Action)(() => _ = new ProvisioningSettings(values, new OidcSettings(values)))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GroupsClaim_IsNotCheckedWhileJitIsOff()
    {
        var values = Enabled with { GroupsClaim = "sub" };
        ((Action)(() => _ = new ProvisioningSettings(values, new OidcSettings(values)))).Should().NotThrow();
    }
}
