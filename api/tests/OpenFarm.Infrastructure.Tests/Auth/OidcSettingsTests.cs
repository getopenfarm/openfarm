using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public class OidcSettingsTests
{
    [Fact]
    public void Disabled_DefaultsPreservePasswordLogin()
    {
        var settings = Build();
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
    public void Enabled_RejectsInvalidAuthorities(string authority)
    {
        var action = () => Build(("Enabled", "true"), ("Authority", authority), ("ClientId", "farm"), ("ClientSecret", "credential"));
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Required_OnlyExplicitRecoveryUsersCanUsePasswordsOrOldPasswordSessions()
    {
        var recovery = Guid.NewGuid();
        var settings = Enabled(("Required", "true"), ("RecoveryUserIds:0", recovery.ToString()));
        settings.AllowsPassword(recovery).Should().BeTrue();
        settings.AllowsBrowserSession(recovery, null).Should().BeTrue();
        settings.AllowsPassword(Guid.NewGuid()).Should().BeFalse();
        settings.AllowsBrowserSession(Guid.NewGuid(), null).Should().BeFalse();
        settings.AllowsBrowserSession(Guid.NewGuid(), settings.SessionIdentity).Should().BeTrue();
    }

    [Fact]
    public void ChangingClient_PreservesIdentityLinksButInvalidatesExternalSessions()
    {
        var old = Enabled();
        var changed = Enabled(("ClientId", "replacement"));
        changed.ProviderKey.Should().Be(old.ProviderKey);
        changed.AllowsBrowserSession(Guid.NewGuid(), old.SessionIdentity).Should().BeFalse();
    }

    [Fact]
    public void Required_RejectsMissingProviderOrRecoveryAccount()
    {
        ((Action)(() => Build(("Required", "true")))).Should().Throw<InvalidOperationException>();
        ((Action)(() => Enabled(("Required", "true")))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("25")]
    public void RejectsUnboundedExternalSessionLifetimes(string hours) =>
        ((Action)(() => Enabled(("SessionHours", hours)))).Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData(null)]
    [InlineData("http://farm.example")]
    public void Enabled_RequiresASecurePublicUrl(string? value) =>
        ((Action)(() => Enabled().ValidatePublicUrl(value))).Should().Throw<InvalidOperationException>();

    private static OidcSettings Enabled(params (string Key, string Value)[] overrides) => Build(
        new[] { ("Enabled", "true"), ("Authority", "https://idp.example/application/o/farm/"), ("ClientId", "farm"), ("ClientSecret", "credential") }
            .Concat(overrides).ToArray());

    private static OidcSettings Build(params (string Key, string Value)[] values)
    {
        var dictionary = new Dictionary<string, string?>();
        foreach (var (key, value) in values) dictionary["Auth:Oidc:" + key] = value;
        return new(new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build());
    }
}
