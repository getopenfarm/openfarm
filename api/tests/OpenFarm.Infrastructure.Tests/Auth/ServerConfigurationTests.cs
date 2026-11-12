using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OpenFarm.Domain.Settings;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public sealed class ServerConfigurationTests
{
    private static ServerConfigurationOverrides Policy(params (string Key, string? Value)[] values) => new(
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build());

    [Fact]
    public void DeploymentDisabledOverrideWins_AndLegacyOAuthNeverOverridesASavedDisable()
    {
        var saved = new ServerSettings();
        saved.Update("https://saved.test", McpAccessMode.SignIn);
        var forced = Policy(("MCP_ENABLED", "false"), ("MCP_MODE", "SignIn"), ("ORIGIN", "https://managed.test"));
        forced.Resolve(saved).McpEnabled.Should().BeFalse();
        forced.Resolve(saved).OAuth.Enabled.Should().BeFalse();
        forced.Resolve(saved).PublicUrl.Should().Be("https://managed.test");
        saved.Update("https://saved.test", McpAccessMode.Disabled);
        Policy(("OAUTH_ENABLED", "true"), ("ORIGIN", "https://legacy.test")).Resolve(saved).McpEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, McpAccessMode.Disabled, McpAccessMode.Disabled)]
    [InlineData(null, McpAccessMode.SignIn, McpAccessMode.SignIn)]
    [InlineData("true", McpAccessMode.Disabled, McpAccessMode.AccessTokens)]
    [InlineData("true", McpAccessMode.SignIn, McpAccessMode.SignIn)]
    [InlineData("false", McpAccessMode.SignIn, McpAccessMode.Disabled)]
    public void EnabledOverrideUsesPositiveSemantics(string? enabled, McpAccessMode savedMode, McpAccessMode expected)
    {
        var saved = new ServerSettings();
        saved.Update("https://farm.test", savedMode);
        Policy(("MCP_ENABLED", enabled)).Resolve(saved).McpMode.Should().Be(expected);
    }

    [Fact]
    public void ConflictingEnabledAndDisabledModeAreRejected() =>
        FluentActions.Invoking(() => Policy(("MCP_ENABLED", "true"), ("MCP_MODE", "Disabled")))
            .Should().Throw<InvalidOperationException>().WithMessage("MCP_ENABLED=true cannot be combined with MCP_MODE=Disabled.");

    [Theory]
    [InlineData("http://farm.lan", false)]
    [InlineData("http://127.0.0.1:6868", true)]
    [InlineData("https://farm.test", true)]
    [InlineData(null, false)]
    public void SavedAndDeploymentSignInHaveTheSameHttpsRequirement(string? origin, bool valid)
    {
        var saved = new ServerSettings();
        saved.Update(origin, McpAccessMode.SignIn);
        Action resolve = () => Policy().Resolve(saved);
        if (valid) resolve.Should().NotThrow();
        else resolve.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("MCP_MODE", "enabled")]
    [InlineData("MCP_MODE", "1")]
    [InlineData("MCP_ENABLED", "yes")]
    [InlineData("ORIGIN", "https://farm.test/app/..")]
    public void InvalidDeploymentValuesFailClosed(string key, string value) =>
        FluentActions.Invoking(() => Policy((key, value))).Should().Throw<InvalidOperationException>();
}
