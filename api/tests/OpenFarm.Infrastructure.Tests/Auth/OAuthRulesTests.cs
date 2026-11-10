using FluentAssertions;
using Microsoft.Extensions.Primitives;
using OpenFarm.Infrastructure.Auth.OAuth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public sealed class OAuthRulesTests
{
    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", RedirectKind.Web)]
    [InlineData("https://chatgpt.com/connector_platform_oauth_redirect?x=1", RedirectKind.Web)]
    [InlineData("http://localhost:8765/cb", RedirectKind.Loopback)]
    [InlineData("http://127.0.0.1/cb", RedirectKind.Loopback)]
    [InlineData("http://[::1]:9/cb", RedirectKind.Loopback)]
    [InlineData("cursor://anysphere.cursor-mcp/oauth/callback", RedirectKind.Native)]
    [InlineData("vscode://vscode.github-authentication/did-authenticate", RedirectKind.Native)]
    [InlineData("vscode-insiders://vscode.github-authentication/did-authenticate", RedirectKind.Native)]
    public void ClassifyRedirect_Accepts(string uri, RedirectKind kind) => OAuthRules.ClassifyRedirect(uri).Should().Be(kind);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative/cb")]
    [InlineData("http://farm.local/cb")]
    [InlineData("http://localhost.evil.test/cb")]
    [InlineData("com.example.app:/cb")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ms-msdt:/id")]
    [InlineData("vscode-evil://x/cb")]
    [InlineData("https://claude.ai/cb#frag")]
    [InlineData("https://user:pass@claude.ai/cb")]
    [InlineData("https://user@claude.ai/cb")]
    [InlineData("https://claude.ai/c b")]
    [InlineData("https://claude.ai/cb\n")]
    [InlineData("https:\\\\claude.ai\\cb")]
    public void ClassifyRedirect_Rejects(string? uri) => OAuthRules.ClassifyRedirect(uri).Should().BeNull();

    [Fact]
    public void ClassifyRedirect_RejectsOverlongUris()
    {
        var ok = "https://claude.ai/" + new string('a', 2048 - "https://claude.ai/".Length);
        OAuthRules.ClassifyRedirect(ok).Should().Be(RedirectKind.Web);
        OAuthRules.ClassifyRedirect(ok + "a").Should().BeNull();
    }

    [Theory]
    [InlineData("https://claude.ai/cb", "https://claude.ai/cb", true)]
    [InlineData("https://claude.ai/cb", "https://claude.ai/cb/", false)]
    [InlineData("https://claude.ai/cb", "https://claude.ai:8443/cb", false)]
    [InlineData("https://claude.ai/cb", "https://CLAUDE.ai/cb", false)]
    [InlineData("http://127.0.0.1:1111/cb", "http://127.0.0.1:2222/cb", true)]
    [InlineData("http://127.0.0.1/cb", "http://127.0.0.1:2222/cb", true)]
    [InlineData("http://[::1]:1/cb", "http://[::1]:2/cb", true)]
    [InlineData("http://localhost:1/cb", "http://localhost:2/cb", true)]
    [InlineData("http://localhost:1/cb", "http://127.0.0.1:1/cb", false)]
    [InlineData("http://localhost:1/cb", "http://localhost:1/other", false)]
    [InlineData("http://localhost:1/cb?a=1", "http://localhost:2/cb?a=2", false)]
    [InlineData("http://localhost:1/cb", "http://localhost:2/cb#x", false)]
    [InlineData("cursor://anysphere.cursor-mcp/cb", "cursor://anysphere.cursor-mcp/cb", true)]
    [InlineData("cursor://anysphere.cursor-mcp/cb", "cursor://other/cb", false)]
    public void RedirectMatches(string registered, string requested, bool expected) =>
        OAuthRules.RedirectMatches(registered, requested).Should().Be(expected);

    [Fact]
    public void ReturnsToLabels()
    {
        OAuthRules.ReturnsTo("https://claude.ai/cb").Should().Be(new ReturnsTo("Web", "claude.ai"));
        OAuthRules.ReturnsTo("https://xn--clude-sqa.ai/cb").Label.Should().Be("xn--clude-sqa.ai");
        OAuthRules.ReturnsTo("https://clàude.ai/cb").Label.Should().Be("xn--clude-sqa.ai");
        OAuthRules.ReturnsTo("http://127.0.0.1:5/cb").Should().Be(new ReturnsTo("Loopback", "this computer (localhost)"));
        OAuthRules.ReturnsTo("cursor://anysphere.cursor-mcp/cb").Should().Be(new ReturnsTo("Native", "an app on this device (cursor:)"));
    }

    [Fact]
    public void Pkce_Rfc7636AppendixB()
    {
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        OAuthRules.IsValidChallenge(challenge).Should().BeTrue();
        OAuthRules.VerifyPkce("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", challenge).Should().BeTrue();
        OAuthRules.VerifyPkce("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXl", challenge).Should().BeFalse();
        // plain: the verifier itself as the challenge is never accepted.
        OAuthRules.VerifyPkce(challenge, challenge).Should().BeFalse();
        OAuthRules.VerifyPkce(null, challenge).Should().BeFalse();
    }

    [Theory]
    [InlineData(42, false)]
    [InlineData(43, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Pkce_VerifierLength(int length, bool syntacticallyValid)
    {
        var verifier = new string('a', length);
        var challenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        OAuthRules.VerifyPkce(verifier, challenge).Should().Be(syntacticallyValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-c")]
    [InlineData("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cMM")]
    [InlineData("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw+cM")]
    public void InvalidChallenges(string? challenge) => OAuthRules.IsValidChallenge(challenge).Should().BeFalse();

    [Theory]
    [InlineData(null, "MCP client")]
    [InlineData("   ", "MCP client")]
    [InlineData("  Claude  ", "Claude")]
    [InlineData("Claude Code 🤖", "Claude Code 🤖")]
    [InlineData("Cafe\u0301 app", "Caf\u00e9 app")]
    public void ClientNameNormalizes(string? name, string expected) => OAuthRules.NormalizeClientName(name).Should().Be(expected);

    [Theory]
    [InlineData("Claude\u202Eedoc")]
    [InlineData("Clau\u200Bde")]
    [InlineData("Claude\u2066x")]
    [InlineData("Claude\u0007")]
    [InlineData("Claude\nCode")]
    [InlineData("Claude\u007F")]
    [InlineData("Claude\u2028x")]
    [InlineData("Claude\u20DD")]
    [InlineData("Claude\uE000")]
    [InlineData("Claude\u0378")]
    [InlineData("Claude\u115F")]
    [InlineData("Claude\u1160")]
    [InlineData("Claude\u3164")]
    [InlineData("Claude\uFFA0")]
    [InlineData("Claude\u2800")]
    [InlineData("OpenFarm")]
    [InlineData("openfarm")]
    [InlineData(" Open Farm ")]
    [InlineData("OPEN FARM")]
    public void ClientNameRefusesInvisibleUnassignedAndLookalikeNames(string name) => OAuthRules.NormalizeClientName(name).Should().BeNull();

    [Fact]
    public void ClientNameLimit()
    {
        OAuthRules.NormalizeClientName(new string('a', 100)).Should().HaveLength(100);
        OAuthRules.NormalizeClientName(new string('a', 101)).Should().BeNull();
        OAuthRules.NormalizeClientName("Claude" + (char)0xD800).Should().BeNull();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("abc ~!", true)]
    [InlineData("", false)]
    [InlineData("café", false)]
    [InlineData("a\tb", false)]
    public void State(string? state, bool valid) => OAuthRules.ValidState(state).Should().Be(valid);

    [Fact]
    public void StateLimit()
    {
        OAuthRules.ValidState(new string('s', 1024)).Should().BeTrue();
        OAuthRules.ValidState(new string('s', 1025)).Should().BeFalse();
    }

    [Fact]
    public void ParametersRefuseDuplicatesAndOverlongValues()
    {
        OAuthRules.ParseParameters([new("a", "1"), new("b", "2")]).Should().BeEquivalentTo(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        OAuthRules.ParseParameters([new("resource", new StringValues(["a", "b"]))]).Should().BeNull();
        OAuthRules.ParseParameters([new("a", "1"), new("a", "2")]).Should().BeNull();
        OAuthRules.ParseParameters([new("a", new string('x', 4096))]).Should().NotBeNull();
        OAuthRules.ParseParameters([new("a", new string('x', 4097))]).Should().BeNull();
        // RFC 6749 §3.1: a parameter sent without a value is treated as omitted.
        OAuthRules.ParseParameters([new("a", ""), new("b", "2")]).Should().BeEquivalentTo(new Dictionary<string, string> { ["b"] = "2" });
        OAuthRules.ParseParameters([new("a", new StringValues(["", "x"]))]).Should().BeNull();
    }
}
