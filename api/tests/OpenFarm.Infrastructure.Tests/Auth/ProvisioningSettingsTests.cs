using System.Text.Json.Nodes;
using FluentAssertions;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public class ProvisioningSettingsTests
{
    [Fact]
    public void DefaultsKeepAutomaticProvisioningDisabled()
    {
        var settings = Build();
        settings.JitEnabled.Should().BeFalse(); settings.ScimEnabled.Should().BeFalse();
        settings.RequireVerifiedEmail.Should().BeTrue(); settings.DefaultGroup.Should().Be("Viewers");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProvisioningRequiresAnOidcProvider(bool jit) =>
        ((Action)(() => Build(new() { JitEnabled = jit, ScimEnabled = !jit }))).Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("a-long-but-not-valid-provisioning token")]
    [InlineData("ofp_0123456789abcdef0123456789abcdef")]
    [InlineData("ofr_0123456789abcdef0123456789abcdef")]
    [InlineData("eyJheader0123456789.payload0123456789.signature0123456789")]
    public void ScimRequiresAStrongSeparateCredential(string credential) =>
        ((Action)(() => Build(Enabled with { ScimEnabled = true, ScimToken = credential }))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void ScimRejectsReusingTheOidcClientSecret() =>
        ((Action)(() => Build(Enabled with { ScimEnabled = true, ScimToken = new string('x', 32), ClientSecret = new string('x', 32) }))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void GroupMappingsAreExplicitAndCaseInsensitive()
    {
        var settings = Build(Enabled with { ScimEnabled = true, ScimToken = new string('x', 32), GroupMappings = new() { ["farm-operators"] = "Operators" } });
        settings.GroupMappings["FARM-OPERATORS"].Should().Be("Operators");
        settings.GroupMappings.ContainsKey("Administrators").Should().BeFalse();
    }

    [Fact]
    public void ScimFilterParsesEscapedStrings_WithoutAcceptingAdditionalExpressions()
    {
        var filter = ScimProtocol.Filter("userName eq \"a\\\"b\"", false);
        filter!.Value.Value.Should().Be("a\"b");
        ((Action)(() => ScimProtocol.Filter("userName eq \"a\" or active eq true", false))).Should().Throw<ScimException>();
    }

    [Fact]
    public void FailedPatchCannotChangeTheOriginalResource()
    {
        var original = new JsonObject { ["schemas"] = new JsonArray(ScimProtocol.UserSchema), ["active"] = true };
        var patch = new JsonObject
        {
            ["schemas"] = new JsonArray(ScimProtocol.PatchSchema),
            ["Operations"] = new JsonArray(
            new JsonObject { ["op"] = "replace", ["path"] = "active", ["value"] = false },
            new JsonObject { ["op"] = "replace", ["path"] = "password", ["value"] = "must-not-be-accepted" })
        };
        ((Action)(() => ScimPatch.Apply(original, patch, false))).Should().Throw<ScimException>();
        original["active"]!.GetValue<bool>().Should().BeTrue();
    }

    private static AuthenticationSettingsValues Enabled => new()
    {
        OidcEnabled = true,
        Authority = "https://identity.example/application/o/farm/",
        ClientId = "farm",
        ClientSecret = "oidc-client-credential",
    };
    private static ProvisioningSettings Build(AuthenticationSettingsValues? value = null)
    {
        var settings = value ?? new();
        return new(settings, new OidcSettings(settings));
    }
}
