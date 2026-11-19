using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using OpenFarm.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class OidcLoginTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Disabled_DoesNotExposeChallengesOrProviderCredentials()
    {
        using var client = factory.CreateClient();
        var options = await client.GetFromJsonAsync<LoginProvidersResponse>("/api/v1/auth/providers");
        options!.OidcEnabled.Should().BeFalse();
        (await client.GetAsync("/api/v1/auth/sso/login")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/auth/sso/link")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProviderTest_ProvesLinkedAdministratorAndPreservesOriginalBrowserSession()
    {
        await using var kit = new OidcKit(factory, jit: true);
        using var browser = kit.CreateClient();
        var userId = await kit.AuthenticateAsync(browser, administrator: true);
        var subject = Guid.NewGuid().ToString();
        await BeginLinkAsync(browser);
        var link = await kit.CallbackAsync(browser, "/api/v1/auth/sso/link", subject);
        (await browser.GetAsync(link)).Headers.Location!.OriginalString.Should().Contain("sso=linked");
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var sessionsBefore = await db.BrowserSessions.CountAsync(s => s.UserId == userId);
        (await CsrfPostAsync(browser, "/api/v1/admin/settings/authentication/test")).EnsureSuccessStatusCode();
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/test", subject);
        var tested = await browser.GetAsync(callback);
        tested.Headers.Location!.OriginalString.Should().Be("/settings/authentication?test=success");
        tested.Headers.GetValues("Set-Cookie").Should().NotContain(c => c.StartsWith("openfarm.refresh=", StringComparison.Ordinal));
        (await db.BrowserSessions.CountAsync(s => s.UserId == userId)).Should().Be(sessionsBefore);
        (await browser.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami"))!.UserId.Should().Be(userId);
        (await db.AuthenticationSettings.AsNoTracking().SingleAsync()).TestedProviderIdentity.Should()
            .Be(scope.ServiceProvider.GetRequiredService<OidcSettings>().TestIdentity("https://farm.example"));
        (await browser.GetAsync("/api/v1/auth/sso/test")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderTest_RejectsWrongIdentityOrRevokedOriginalSession_WithoutJitCreation(bool revoke)
    {
        await using var kit = new OidcKit(factory, jit: true);
        using var browser = kit.CreateClient();
        var userId = await kit.AuthenticateAsync(browser, administrator: true);
        (await CsrfPostAsync(browser, "/api/v1/admin/settings/authentication/test")).EnsureSuccessStatusCode();
        var subject = "test-unlinked-" + Guid.NewGuid().ToString("N");
        kit.Provider.Profile = new(subject, subject + "@example.test", "Unexpected identity", true);
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/test", subject);
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        if (revoke) await db.BrowserSessions.Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, DateTimeOffset.UtcNow));
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/settings/authentication?test=failed");
        (await db.Users.AnyAsync(u => u.ProvisioningExternalId == subject)).Should().BeFalse();
    }

    [Fact]
    public async Task ProviderTest_RequiresAnAdministratorBrowserSession()
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        await kit.AuthenticateAsync(browser);
        (await CsrfPostAsync(browser, "/api/v1/admin/settings/authentication/test")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Jit_ProvisionsAPasswordlessViewer_AndPreservesTheIdentityAcrossProfileChanges()
    {
        await using var kit = new OidcKit(factory, jit: true);
        var suffix = Guid.NewGuid().ToString("N");
        var subject = "jit-" + suffix;
        kit.Provider.Profile = new("jit-" + suffix, suffix + "@example.test", "New colleague", true);
        using var browser = kit.CreateClient();
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", subject);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().StartWith("/auth/complete");
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.ProvisioningExternalId == subject);
        user.PasswordHash.Should().BeNull();
        user.ProvisioningProvider.Should().Be(scope.ServiceProvider.GetRequiredService<OidcSettings>().ProviderKey);
        var access = await scope.ServiceProvider.GetRequiredService<PermissionService>().ResolveAsync(user.Id, CancellationToken.None);
        access.Groups.Should().Contain("Viewers");
        access.IsAdministrator.Should().BeFalse();
        kit.Provider.Profile = kit.Provider.Profile with { Email = "changed-" + suffix + "@example.test", Username = "renamed-" + suffix };
        using var next = kit.CreateClient();
        callback = await kit.CallbackAsync(next, "/api/v1/auth/sso/login", subject);
        (await next.GetAsync(callback)).Headers.Location!.OriginalString.Should().StartWith("/auth/complete");
        (await db.Users.CountAsync(u => u.ProvisioningExternalId == subject)).Should().Be(1);
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Email.Should().Be(user.Email);
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsEnabled, false));
        callback = await kit.CallbackAsync(next, "/api/v1/auth/sso/login", subject);
        (await next.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
    }

    [Fact]
    public async Task JitAccounts_CanBeRemovedLocally_AndEnrollAgainWithBaselineAccess()
    {
        await using var kit = new OidcKit(factory, jit: true);
        var subject = "jit-remove-" + Guid.NewGuid().ToString("N");
        kit.Provider.Profile = new(subject, subject + "@example.test", "New colleague", true);
        using var browser = kit.CreateClient();
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", subject);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().StartWith("/auth/complete");
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var original = await db.Users.AsNoTracking().SingleAsync(u => u.ProvisioningExternalId == subject);
        using var administrator = kit.CreateClient();
        await kit.AuthenticateAsync(administrator, administrator: true);
        (await administrator.DeleteAsync("/api/v1/admin/users/" + original.Id)).EnsureSuccessStatusCode();
        (await db.UserLogins.AnyAsync(l => l.UserId == original.Id)).Should().BeFalse();
        using var next = kit.CreateClient();
        callback = await kit.CallbackAsync(next, "/api/v1/auth/sso/login", subject);
        (await next.GetAsync(callback)).Headers.Location!.OriginalString.Should().StartWith("/auth/complete");
        var replacement = await db.Users.AsNoTracking().SingleAsync(u => u.ProvisioningExternalId == subject);
        replacement.Id.Should().NotBe(original.Id);
        replacement.PasswordHash.Should().BeNull();
        var access = await scope.ServiceProvider.GetRequiredService<PermissionService>().ResolveAsync(replacement.Id, CancellationToken.None);
        access.Groups.Should().Equal("Viewers");
        access.IsAdministrator.Should().BeFalse();
    }

    [Fact]
    public async Task Jit_RejectsUnverifiedEmailAndAccountCollisions()
    {
        await using var kit = new OidcKit(factory, jit: true);
        using var browser = kit.CreateClient();
        var username = "unverified-" + Guid.NewGuid().ToString("N");
        kit.Provider.Profile = new(username, username + "@example.test", "Unverified", false);
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", username);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
        kit.Provider.Profile = new(username, "admin@openfarm.local", "Collision", true);
        callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", username);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
        using var scope = kit.App.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Users.CountAsync(u => u.ProvisioningExternalId == username)).Should().Be(0);
    }

    [Fact]
    public async Task LinkThenLogin_ReusesLocalAccountPermissionsAndSessionLifecycle()
    {
        await using var kit = new OidcKit(factory);
        using var owner = kit.CreateClient();
        await kit.AuthenticateAsync(owner);
        var before = await owner.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami");
        var subject = Guid.NewGuid().ToString();
        await BeginLinkAsync(owner);
        var callback = await kit.CallbackAsync(owner, "/api/v1/auth/sso/link", subject);
        (await owner.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=linked", kit.Provider.LastFailure);
        // A linking request cannot be replayed, even with a new provider authorization code.
        (await owner.GetAsync("/api/v1/auth/sso/link")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var browser = kit.CreateClient();
        var signInCallback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login?returnTo=%2Fconnect%3Frequest%3Dexample", subject);
        var signedIn = await browser.GetAsync(signInCallback);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.OriginalString.Should().Be("/auth/complete?returnTo=%2Fconnect%3Frequest%3Dexample");
        signedIn.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("openfarm.refresh=", StringComparison.Ordinal)
            && c.Contains("secure", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        signedIn.Headers.Location.OriginalString.Should().NotContain("token");

        var refreshed = await CsrfPostAsync(browser, "/api/v1/auth/refresh");
        refreshed.EnsureSuccessStatusCode();
        var login = (await refreshed.Content.ReadFromJsonAsync<LoginResponse>())!;
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var after = await browser.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami");
        after.Should().BeEquivalentTo(before);
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(login.Token).Claims.Single(c => c.Type == "sid").Value);
        var session = await db.BrowserSessions.SingleAsync(s => s.Id == sid);
        session.OidcConfigurationIdentity.Should().NotBeNull();
        session.ExpiresAtUtc.Should().BeCloseTo(session.CreatedAtUtc.AddHours(8), TimeSpan.FromSeconds(1));
        (await CsrfPostAsync(browser, "/api/v1/auth/logout")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        kit.Provider.Exchanges.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    public async Task InvalidProviderProof_NeverCreatesASession(string invalid)
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", "unknown", invalid);
        var response = await browser.GetAsync(callback);
        response.Headers.Location!.OriginalString.Should().Be("/login?ssoError=failed");
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).Should().NotContain(c => c.StartsWith("openfarm.refresh=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("auth-time-missing")]
    [InlineData("auth-time-stale")]
    [InlineData("auth-time-future")]
    [InlineData("auth-time-malformed")]
    [InlineData("auth-time-duplicate")]
    public async Task Linking_RejectsInvalidAuthenticationTime(string invalid)
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        await kit.AuthenticateAsync(browser);
        await BeginLinkAsync(browser);
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/link", Guid.NewGuid().ToString(), invalid);
        var response = await browser.GetAsync(callback);
        // A claim that is absent, unreadable or duplicated cannot be fixed by retrying: it gets its own reason.
        var unusable = invalid is "auth-time-missing" or "auth-time-malformed" or "auth-time-duplicate";
        response.Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=failed&ssoReason="
            + (unusable ? "auth_time_missing" : "fresh_authentication"));
        (await browser.GetFromJsonAsync<OidcIdentityResponse>("/api/v1/auth/sso/identity"))!.Linked.Should().BeFalse();
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PushedAuthorization_PreservesLinkingProofAndLogin()
    {
        await using var kit = new OidcKit(factory, par: true);
        using var browser = kit.CreateClient();
        await kit.AuthenticateAsync(browser);
        await BeginLinkAsync(browser);
        var subject = Guid.NewGuid().ToString();
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/link", subject);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=linked");
        using var next = kit.CreateClient();
        callback = await kit.CallbackAsync(next, "/api/v1/auth/sso/login", subject);
        (await next.GetAsync(callback)).Headers.Location!.OriginalString.Should().StartWith("/auth/complete");
        kit.Provider.Pushes.Should().Be(2);
    }

    [Fact]
    public async Task BackchannelLogout_RevokesOnlyTheMatchingSsoSession_AndIsIdempotent()
    {
        await using var kit = new OidcKit(factory);
        using var owner = kit.CreateClient();
        await kit.AuthenticateAsync(owner);
        var subject = Guid.NewGuid().ToString();
        await BeginLinkAsync(owner);
        (await owner.GetAsync(await kit.CallbackAsync(owner, "/api/v1/auth/sso/link", subject))).Headers.Location!.OriginalString.Should().EndWith("sso=linked");
        using var first = kit.CreateClient();
        kit.Provider.SessionId = "first-" + subject;
        await SsoSessionAsync(kit, first, subject);
        using var second = kit.CreateClient();
        kit.Provider.SessionId = "second-" + subject;
        await SsoSessionAsync(kit, second, subject);
        using var sender = kit.CreateClient();
        var token = LogoutToken(kit.Provider, subject, "first-" + subject);
        (await PostLogoutAsync(sender, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostLogoutAsync(sender, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        // Even a newly issued code for the terminated IdP sid cannot re-open a session.
        kit.Provider.SessionId = "first-" + subject;
        using var stale = kit.CreateClient();
        (await stale.GetAsync(await kit.CallbackAsync(stale, "/api/v1/auth/sso/login", subject))).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
    }

    [Fact]
    public async Task SubjectLogout_RevokesAllProviderSessions_AndBlocksAnInflightOldAuthentication()
    {
        await using var kit = new OidcKit(factory, jit: true);
        var suffix = Guid.NewGuid().ToString("N");
        kit.Provider.Profile = new("logout-" + suffix, suffix + "@example.test", "Logout", true);
        using var browser = kit.CreateClient();
        await SsoSessionAsync(kit, browser, suffix);
        using var sender = kit.CreateClient();
        var pending = await kit.CallbackAsync(sender, "/api/v1/auth/sso/login", suffix);
        (await PostLogoutAsync(sender, LogoutToken(kit.Provider, suffix, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await sender.GetAsync(pending)).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("nonce")]
    [InlineData("events")]
    [InlineData("jti")]
    [InlineData("identity")]
    [InlineData("iat")]
    public async Task BackchannelLogout_RejectsInvalidProof(string invalid)
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        (await PostLogoutAsync(browser, LogoutToken(kit.Provider, "subject", "session", invalid))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task SsoSessionAsync(OidcKit kit, HttpClient browser, string subject)
    {
        (await browser.GetAsync(await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", subject))).Headers.Location!.OriginalString.Should().StartWith("/auth/complete");
        var refreshed = await CsrfPostAsync(browser, "/api/v1/auth/refresh");
        refreshed.EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await refreshed.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
    }

    private static Task<HttpResponseMessage> PostLogoutAsync(HttpClient browser, string token) => browser.PostAsync(
        "/api/v1/auth/sso/backchannel-logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = token }));

    private static string LogoutToken(FakeProvider provider, string? subject, string? sid, string? invalid = null)
    {
        var now = DateTime.UtcNow;
        var issued = invalid == "stale" ? now.AddMinutes(-10) : invalid == "future" ? now.AddMinutes(10) : now;
        var claims = new List<Claim>
        {
            new("iat", invalid == "iat" ? "invalid" : EpochTime.GetIntDate(issued).ToString(System.Globalization.CultureInfo.InvariantCulture),
                invalid == "iat" ? ClaimValueTypes.String : ClaimValueTypes.Integer64),
            new("events", invalid == "events" ? "{}" : "{\"http://schemas.openid.net/event/backchannel-logout\":{}}", "JSON"),
        };
        if (invalid != "jti") claims.Add(new("jti", Guid.NewGuid().ToString()));
        if (subject is not null && invalid != "identity") claims.Add(new("sub", subject));
        if (sid is not null && invalid != "identity") claims.Add(new("sid", sid));
        if (invalid == "nonce") claims.Add(new("nonce", "not-a-logout-token"));
        var token = new JwtSecurityToken(invalid == "issuer" ? "https://wrong.example/" : FakeProvider.Issuer,
            invalid == "audience" ? "another-client" : "farm", claims, notBefore: now.AddHours(-1),
            expires: invalid == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            signingCredentials: new SigningCredentials(provider.Key, SecurityAlgorithms.RsaSha256));
        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return invalid == "signature" ? encoded[..(encoded.LastIndexOf('.') + 1)] + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(256)) : encoded;
    }

    [Fact]
    public async Task UnknownIdentityAndInvalidState_DoNotProvisionOrAuthenticate()
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var count = await db.Users.CountAsync();
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", Guid.NewGuid().ToString());
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
        (await db.Users.CountAsync()).Should().Be(count);
        (await browser.GetAsync("/api/v1/auth/sso/callback?code=fake&state=invalid")).Headers.Location!.OriginalString.Should().Be("/login?ssoError=failed");
    }

    [Fact]
    public async Task Linking_RequiresCsrfPasswordAndOriginalBrowser_AndDoesNotSignOutOnBadPassword()
    {
        await using var kit = new OidcKit(factory);
        using var owner = kit.CreateClient();
        await kit.AuthenticateAsync(owner);
        (await owner.PostAsJsonAsync("/api/v1/auth/sso/link", new { password = "admin-openfarm" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var authorization = owner.DefaultRequestHeaders.Authorization;
        owner.DefaultRequestHeaders.Authorization = null;
        var anonymousCsrf = (await owner.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        owner.DefaultRequestHeaders.Authorization = authorization;
        using (var wrongIdentityProof = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sso/link"))
        {
            wrongIdentityProof.Headers.Add("X-CSRF-TOKEN", anonymousCsrf.Token);
            wrongIdentityProof.Content = JsonContent.Create(new { password = "admin-openfarm" });
            (await owner.SendAsync(wrongIdentityProof)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        (await CsrfPostAsync(owner, "/api/v1/auth/sso/link", new { password = "incorrect" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        await BeginLinkAsync(owner);
        using var stranger = kit.CreateClient();
        (await stranger.GetAsync("/api/v1/auth/sso/link")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var callback = await kit.CallbackAsync(owner, "/api/v1/auth/sso/link", Guid.NewGuid().ToString());
        // Revocation between password proof and callback invalidates linking.
        (await CsrfPostAsync(owner, "/api/v1/auth/logout")).EnsureSuccessStatusCode();
        (await owner.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=failed&ssoReason=session_changed");
    }

    private static async Task BeginLinkAsync(HttpClient client) =>
        (await CsrfPostAsync(client, "/api/v1/auth/sso/link", new { password = "admin-openfarm" })).EnsureSuccessStatusCode();

    [Fact]
    public async Task LinkProof_IsConsumedOnceAcrossRequests()
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        var userId = await kit.AuthenticateAsync(browser);
        string proof;
        string credential;
        using (var scope = kit.App.Services.CreateScope())
        {
            var session = await scope.ServiceProvider.GetRequiredService<BrowserSessionService>().CreateAsync(userId, "Link proof", CancellationToken.None);
            var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims.Single(c => c.Type == "sid").Value);
            credential = session.RefreshToken!;
            proof = (await scope.ServiceProvider.GetRequiredService<OidcAccountService>()
                .BeginLinkAsync(userId, sid, "admin-openfarm", credential, CancellationToken.None))!;
        }
        using (var scope = kit.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OidcAccountService>().CompleteLinkAsync(proof, credential, "one-use-" + userId, CancellationToken.None);
        using (var scope = kit.App.Services.CreateScope())
            (await Assert.ThrowsAsync<OidcLinkException>(() => scope.ServiceProvider.GetRequiredService<OidcAccountService>()
                .CompleteLinkAsync(proof, credential, "replacement-" + userId, CancellationToken.None))).Failure.Should().Be(OidcLinkFailure.RequestReplaced);
    }

    [Fact]
    public async Task RequiredMode_EnforcesLoginAndSessionPolicyAndPreservesRecovery()
    {
        await using var kit = new OidcKit(factory, required: true);
        using var browser = kit.CreateClient();
        using var scope = kit.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var username = "required-" + Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = username, Email = username + "@example.com" };
        (await users.CreateAsync(user, "admin-openfarm")).Succeeded.Should().BeTrue();
        (await browser.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(username, "admin-openfarm"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var sessions = scope.ServiceProvider.GetRequiredService<BrowserSessionService>();
        var old = await sessions.CreateAsync(user.Id, "Old password session", CancellationToken.None);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", old.Token);
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await sessions.RefreshAsync(old.RefreshToken!, CancellationToken.None)).Should().BeNull();
        await sessions.ApplyLoginPolicyAsync(CancellationToken.None);
        var oldSid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(old.Token).Claims.Single(c => c.Type == "sid").Value);
        (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().BrowserSessions.AsNoTracking().SingleAsync(s => s.Id == oldSid))
            .RevokedAtUtc.Should().NotBeNull();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.ResetPasswordAsync(user.Id, "replacement-password", kit.RecoveryId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.ChangePasswordAsync(user.Id, new PasswordInput("admin-openfarm", "replacement-password"), null, CancellationToken.None));
        using var recovery = kit.CreateClient();
        (await recovery.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var subject = Guid.NewGuid().ToString();
        var settings = scope.ServiceProvider.GetRequiredService<OidcSettings>();
        (await users.AddLoginAsync(user, new UserLoginInfo(settings.ProviderKey, subject, "authentik"))).Succeeded.Should().BeTrue();
        var callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", subject);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().StartWith("/auth/complete");
        var response = await CsrfPostAsync(browser, "/api/v1/auth/refresh");
        response.EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var identity = await browser.GetFromJsonAsync<OidcIdentityResponse>("/api/v1/auth/sso/identity");
        identity!.Linked.Should().BeTrue();
        identity.PasswordAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task RecoveryAdministrator_CannotBeDisabledOrRemovedFromAdministratorGroup()
    {
        await using var kit = new OidcKit(factory, required: true);
        using var scope = kit.App.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        var user = (await accounts.ListUsersAsync(CancellationToken.None)).Single(u => u.Id == kit.RecoveryId);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var username = "other-admin-" + Guid.NewGuid().ToString("N");
        var other = new ApplicationUser { UserName = username, Email = username + "@example.com" };
        (await users.CreateAsync(other, "admin-openfarm")).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync(other, PermissionCatalog.Administrators)).Succeeded.Should().BeTrue();
        var input = new AccountInput(user.Username, user.Email, user.Name, user.Groups.Select(g => g.Id).ToArray(), [], false, null);
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.SaveUserAsync(user.Id, input, user.Id, CancellationToken.None));
        using var secondScope = kit.App.Services.CreateScope();
        await Assert.ThrowsAsync<ArgumentException>(() => secondScope.ServiceProvider.GetRequiredService<AccountAdministrationService>()
            .SaveUserAsync(user.Id, input with { IsEnabled = true, GroupIds = [] }, user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task IdentityCollision_DoesNotMergeAccounts_AndDisabledUserCannotSignIn()
    {
        await using var kit = new OidcKit(factory);
        using var owner = kit.CreateClient();
        var userId = await kit.AuthenticateAsync(owner);
        var subject = Guid.NewGuid().ToString();
        await BeginLinkAsync(owner);
        var callback = await kit.CallbackAsync(owner, "/api/v1/auth/sso/link", subject);
        (await owner.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=linked");
        using var other = kit.CreateClient();
        await kit.AuthenticateAsync(other);
        await BeginLinkAsync(other);
        callback = await kit.CallbackAsync(other, "/api/v1/auth/sso/link", subject);
        (await other.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=failed&ssoReason=identity_conflict");
        (await other.GetFromJsonAsync<OidcIdentityResponse>("/api/v1/auth/sso/identity"))!.Linked.Should().BeFalse();
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsEnabled, false));
        using var browser = kit.CreateClient();
        callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", subject);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
        (await owner.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(FakeProvider.Issuer)]
    [InlineData("http://localhost:9000/application/o/farm/")]
    public async Task HttpBackend_UsesConfiguredHttpsCallbackAndSecureCorrelationCookies(string authority)
    {
        await using var kit = new OidcKit(factory, authority: authority);
        using var browser = kit.App.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://internal:6868"), AllowAutoRedirect = false });
        var challenge = await browser.GetAsync("/api/v1/auth/sso/login");
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["redirect_uri"].ToString().Should().Be("https://farm.example" + OidcAuthentication.CallbackPath);
        challenge.Headers.GetValues("Set-Cookie").Should().OnlyContain(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LocalHttpApplication_CanUseRemoteHttpsProvider()
    {
        await using var kit = new OidcKit(factory, origin: "http://localhost:6868");
        using var browser = kit.App.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://localhost:6868"), AllowAutoRedirect = false });
        var challenge = await browser.GetAsync("/api/v1/auth/sso/login");
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["redirect_uri"].ToString()
            .Should().Be("http://localhost:6868" + OidcAuthentication.CallbackPath);
        challenge.Headers.GetValues("Set-Cookie").Should().OnlyContain(c => !c.Contains("; secure", StringComparison.OrdinalIgnoreCase)
            && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnabledOidc_PreventsSavingAnInsecureOrMissingPublicUrl()
    {
        await using var kit = new OidcKit(factory);
        using var scope = kit.App.Services.CreateScope();
        var service = new ServerSettingsService(scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>(),
            new ServerConfigurationOverrides(new ConfigurationBuilder().Build()), scope.ServiceProvider.GetRequiredService<OidcSettings>());
        var saved = await service.ReadAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Guid.NewGuid(), saved.Revision, true,
            "http://farm.example", null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Guid.NewGuid(), saved.Revision, true,
            null, null, CancellationToken.None));
    }

    [Fact]
    public async Task DiscoveryOutage_ReturnsToLoginWithoutProtocolDetails()
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        kit.Provider.Unavailable = true;
        var response = await browser.GetAsync("/api/v1/auth/sso/login");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/login?ssoError=provider_configuration");
        response.Headers.Location.OriginalString.Should().NotContain("Provider unavailable");
    }

    [Fact]
    public async Task DiscoveryOutage_ReturnsLinkingToProfileWithAnActionableCategory_AndPreservesTheSession()
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        var id = await kit.AuthenticateAsync(browser);
        await BeginLinkAsync(browser);
        kit.Provider.Unavailable = true;
        var response = await browser.GetAsync("/api/v1/auth/sso/link");
        response.Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=failed&ssoReason=provider_configuration");
        response.Headers.Location.OriginalString.Should().NotContain("Provider unavailable");
        (await browser.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami"))!.UserId.Should().Be(id);
        (await browser.GetFromJsonAsync<OidcIdentityResponse>("/api/v1/auth/sso/identity"))!.Linked.Should().BeFalse();
    }

    [Fact]
    public async Task DiscoveryOutage_ReturnsTheProviderTestToAuthenticationSettings()
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.CreateClient();
        await kit.AuthenticateAsync(browser, administrator: true);
        (await CsrfPostAsync(browser, "/api/v1/admin/settings/authentication/test")).EnsureSuccessStatusCode();
        kit.Provider.Unavailable = true;
        var response = await browser.GetAsync("/api/v1/auth/sso/test");
        response.Headers.Location!.OriginalString.Should().Be("/settings/authentication?test=failed&ssoReason=provider_configuration");
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class UnavailableConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromException<OpenIdConnectConfiguration>(new HttpRequestException("Provider unavailable"));
        public void RequestRefresh() { }
    }

    private static async Task<HttpResponseMessage> CsrfPostAsync(HttpClient client, string path, object? body = null)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class OidcKit : IAsyncDisposable
    {
        public WebApplicationFactory<Program> App { get; }
        public FakeProvider Provider { get; } = new();
        public Guid RecoveryId { get; }

        public OidcKit(CustomWebApplicationFactory parent, bool required = false, bool jit = false, bool par = false,
            string authority = FakeProvider.Issuer, string origin = "https://farm.example")
        {
            if (required)
            {
                using var scope = parent.Services.CreateScope();
                RecoveryId = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Users.Single(u => u.UserName == "admin").Id;
            }
            App = parent.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ORIGIN", origin);
                builder.ConfigureServices(services =>
                {
                    services.AddScoped(_ => new AuthenticationConfiguration(new AuthenticationSettingsValues
                    {
                        OidcEnabled = true,
                        Authority = authority,
                        ClientId = "farm",
                        ClientSecret = "test-client-credential",
                        JitEnabled = jit,
                        InternalAuthenticationEnabled = !required,
                        RecoveryUserIds = required ? [RecoveryId] : [],
                    }));
                    services.PostConfigure<OpenIdConnectOptions>(OidcAuthentication.Scheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = FakeProvider.Issuer,
                        AuthorizationEndpoint = FakeProvider.Issuer + "authorize",
                        TokenEndpoint = FakeProvider.Issuer + "token",
                        PushedAuthorizationRequestEndpoint = par ? FakeProvider.Issuer + "par" : null,
                    };
                    configuration.SigningKeys.Add(Provider.Key);
                    options.ConfigurationManager = Provider.Unavailable ? new UnavailableConfigurationManager()
                        : new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.Backchannel = new HttpClient(Provider);
                    var failure = options.Events.OnRemoteFailure;
                    options.Events.OnRemoteFailure = async context =>
                    {
                        Provider.LastFailure = context.Failure?.ToString();
                        await failure(context);
                    };
                });
                });
            });
        }

        public async Task<Guid> AuthenticateAsync(HttpClient client, bool administrator = false)
        {
            using var scope = App.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var username = "oidc-" + Guid.NewGuid().ToString("N");
            var user = new ApplicationUser { UserName = username, Email = username + "@example.com" };
            (await users.CreateAsync(user, "admin-openfarm")).Succeeded.Should().BeTrue();
            (await users.AddToRoleAsync(user, administrator ? "Administrators" : "Viewers")).Succeeded.Should().BeTrue();
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(username, "admin-openfarm"));
            response.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
            return user.Id;
        }

        public HttpClient CreateClient() => App.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://farm.example"), AllowAutoRedirect = false });

        public async Task<string> CallbackAsync(HttpClient browser, string path, string subject, string? invalid = null)
        {
            var response = await browser.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
            if (query.TryGetValue("request_uri", out var requestUri))
            {
                requestUri.ToString().Should().Be("urn:example:request:" + Provider.Pushes);
                query.Should().NotContainKey("state");
                query = Provider.PushedParameters!;
            }
            if (path == "/api/v1/auth/sso/link")
            {
                query["prompt"].ToString().Should().Be("login");
                query["max_age"].ToString().Should().Be("0");
            }
            query["redirect_uri"].ToString().Should().Be("https://farm.example" + OidcAuthentication.CallbackPath);
            query["code_challenge_method"].ToString().Should().Be("S256");
            var code = Provider.Issue(subject, query["nonce"]!, query["code_challenge"]!, invalid);
            return QueryHelpers.AddQueryString(OidcAuthentication.CallbackPath, new Dictionary<string, string?>
            { ["code"] = code, ["state"] = query["state"] });
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            Provider.Dispose();
        }
    }

    private sealed class FakeProvider : HttpMessageHandler
    {
        public const string Issuer = "https://identity.example/application/o/farm/";
        private readonly RSA _rsa = RSA.Create(2048);
        public RsaSecurityKey Key { get; }
        private readonly Dictionary<string, (string Subject, string Nonce, string Challenge, string? Invalid, DateTime Issued)> _codes = [];
        public string? SessionId { get; set; }
        public bool Unavailable { get; set; }
        public int Exchanges { get; private set; }
        public int Pushes { get; private set; }
        public Dictionary<string, Microsoft.Extensions.Primitives.StringValues>? PushedParameters { get; private set; }
        public string? LastFailure { get; set; }
        public ExternalProfile? Profile { get; set; }
        public FakeProvider() => Key = new(_rsa) { KeyId = "test-key" };

        public string Issue(string subject, string nonce, string challenge, string? invalid)
        {
            var code = Guid.NewGuid().ToString();
            _codes.Add(code, (subject, nonce, challenge, invalid, DateTime.UtcNow));
            return code;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (request.RequestUri!.AbsoluteUri == Issuer + "par")
            {
                form["client_secret"].ToString().Should().Be("test-client-credential");
                PushedParameters = form;
                Pushes++;
                return new(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new { request_uri = "urn:example:request:" + Pushes, expires_in = 90 })
                };
            }
            request.RequestUri.AbsoluteUri.Should().Be(Issuer + "token");
            form["client_secret"].ToString().Should().Be("test-client-credential");
            form["redirect_uri"].ToString().Should().Be("https://farm.example" + OidcAuthentication.CallbackPath);
            if (!_codes.Remove(form["code"]!, out var proof)) return new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };
            Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!))).Should().Be(proof.Challenge);
            Exchanges++;
            var now = DateTime.UtcNow;
            var claims = new List<Claim> { new("sub", proof.Subject), new("nonce", proof.Invalid == "nonce" ? "wrong" : proof.Nonce),
                new("iat", EpochTime.GetIntDate(now).ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64) };
            if (proof.Invalid != "auth-time-missing")
            {
                var authenticated = proof.Invalid switch
                {
                    "auth-time-stale" => now.AddMinutes(-10),
                    "auth-time-future" => now.AddMinutes(10),
                    _ => proof.Issued,
                };
                claims.Add(new("auth_time", proof.Invalid == "auth-time-malformed" ? "invalid"
                    : EpochTime.GetIntDate(authenticated).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    proof.Invalid == "auth-time-malformed" ? ClaimValueTypes.String : ClaimValueTypes.Integer64));
                if (proof.Invalid == "auth-time-duplicate") claims.Add(claims[^1]);
            }
            if (SessionId is not null) claims.Add(new("sid", SessionId));
            if (Profile is { } profile)
            {
                claims.Add(new("preferred_username", profile.Username!)); claims.Add(new("email", profile.Email!));
                claims.Add(new("name", profile.DisplayName ?? "")); claims.Add(new("email_verified", profile.EmailVerified ? "true" : "false", ClaimValueTypes.Boolean));
            }
            var token = new JwtSecurityToken(proof.Invalid == "issuer" ? "https://wrong.example/" : Issuer,
                proof.Invalid == "audience" ? "another-app" : "farm",
                claims,
                notBefore: now.AddHours(-1), expires: proof.Invalid == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
                signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.RsaSha256));
            var encoded = new JwtSecurityTokenHandler().WriteToken(token);
            if (proof.Invalid == "signature") encoded = encoded[..(encoded.LastIndexOf('.') + 1)] + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(256));
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                { access_token = "external-access-token", token_type = "Bearer", expires_in = 300, id_token = encoded })
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _rsa.Dispose();
            base.Dispose(disposing);
        }
    }
}
