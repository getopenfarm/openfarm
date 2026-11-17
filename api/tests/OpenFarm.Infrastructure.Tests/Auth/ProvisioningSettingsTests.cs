using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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
    [InlineData("Auth:Oidc:Jit:Enabled")]
    [InlineData("Auth:Scim:Enabled")]
    public void ProvisioningRequiresAnOidcProvider(string key) =>
        ((Action)(() => Build((key, "true")))).Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("a-long-but-not-valid-provisioning token")]
    [InlineData("ofp_0123456789abcdef0123456789abcdef")]
    [InlineData("ofr_0123456789abcdef0123456789abcdef")]
    [InlineData("eyJheader0123456789.payload0123456789.signature0123456789")]
    public void ScimRequiresAStrongSeparateCredential(string credential) =>
        ((Action)(() => Enabled(("Auth:Scim:Enabled", "true"), ("Auth:Scim:BearerToken", credential)))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void ScimRejectsReusingTheOidcClientSecret() =>
        ((Action)(() => Enabled(("Auth:Scim:Enabled", "true"), ("Auth:Scim:BearerToken", new string('x', 32)),
            ("Auth:Oidc:ClientSecret", new string('x', 32))))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void GroupMappingsAreExplicitAndCaseInsensitive()
    {
        var settings = Enabled(("Auth:Scim:Enabled", "true"), ("Auth:Scim:BearerToken", new string('x', 32)),
            ("Auth:Scim:GroupMappings:farm-operators", "Operators"));
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

    private static ProvisioningSettings Enabled(params (string Key, string Value)[] overrides) => Build(
        new[] { ("Auth:Oidc:Enabled", "true"), ("Auth:Oidc:Authority", "https://identity.example/application/o/farm/"),
            ("Auth:Oidc:ClientId", "farm"), ("Auth:Oidc:ClientSecret", "oidc-client-credential") }.Concat(overrides).ToArray());
    private static ProvisioningSettings Build(params (string Key, string Value)[] values)
    {
        var dictionary = new Dictionary<string, string?>();
        foreach (var (key, value) in values) dictionary[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();
        return new(configuration, new OidcSettings(configuration));
    }
}
