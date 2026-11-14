using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.IntegrationTests.Mcp;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Auth.OAuth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.OAuth;

/// <summary>Revocation triggers, kind separation between browser sessions and apps, and each credential on each surface.</summary>
[Collection("OAuth")]
public sealed class OAuthRevocationTests(OAuthFactory factory)
{
    private readonly OAuthKit _kit = new(factory);

    private sealed class FutureTime(TimeSpan ahead) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + ahead;
    }

    private async Task<(ApplicationUser User, string Password, HttpClient Browser)> UserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "apps-" + Guid.NewGuid().ToString("N")[..10], Email = $"{Guid.NewGuid():N}@example.test" };
        const string password = "apps-password";
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, "Administrators");
        return (user, password, await _kit.BrowserAsync(user.UserName!, password));
    }

    private async Task<string[]> AppRevocationReasonsAsync(Guid appId)
    {
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().ActivityLogEntries.AsNoTracking()
            .Where(a => a.EntityType == "ConnectedApp" && a.EntityId == appId.ToString() && a.EventType == "ConnectedAppRevoked").ToListAsync();
        return rows.Select(r => JsonDocument.Parse(r.Details!.ReportedState!).RootElement.GetProperty("Reason").GetString()!).ToArray();
    }

    [Fact]
    public async Task PasswordChangeResetAndDisableEndApps()
    {
        var (user, password, browser) = await UserAsync();
        var app = await _kit.ConnectAsync(browser);
        (await browser.PostAsJsonAsync("/api/v1/auth/change-password", new PasswordInput(password, "changed-password"))).EnsureSuccessStatusCode();
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await AppRevocationReasonsAsync(app.AppId)).Should().Equal("user");

        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        var admin = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Users.SingleAsync(u => u.UserName == "admin");
        app = await _kit.ConnectAsync(browser);
        await accounts.ResetPasswordAsync(user.Id, "reset-password", admin.Id, CancellationToken.None);
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await AppRevocationReasonsAsync(app.AppId)).Should().Equal("administrator");

        browser = await _kit.BrowserAsync(user.UserName!, "reset-password");
        app = await _kit.ConnectAsync(browser);
        await accounts.SaveUserAsync(user.Id, new(user.UserName!, user.Email!, null, [], [], false, null), admin.Id, CancellationToken.None);
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh)))).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task OwnRevokeAllAndSignOutKeepApps_AdminRevokeAllEndsThem()
    {
        var (user, _, browser) = await UserAsync();
        var app = await _kit.ConnectAsync(browser);

        // Sign out with the app's refresh credential as the cookie: the app stays connected.
        var raw = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        using var csrfResponse = await raw.GetAsync("/api/v1/auth/csrf");
        var antiforgery = string.Join("; ", csrfResponse.Headers.GetValues("Set-Cookie").Select(v => v.Split(';')[0]));
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
        foreach (var path in new[] { "/api/v1/auth/logout", "/api/v1/auth/refresh" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            request.Headers.Add("Cookie", $"{antiforgery}; openfarm.refresh={app.Refresh}");
            using var response = await raw.SendAsync(request);
            response.StatusCode.Should().Be(path.EndsWith("refresh", StringComparison.Ordinal) ? HttpStatusCode.Unauthorized : HttpStatusCode.NoContent);
        }
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.OK);

        (await browser.PostAsync("/api/v1/auth/sessions/revoke-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.OK);
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var admin = await _kit.BrowserAsync();
        (await admin.DeleteAsync($"/api/v1/admin/users/{user.Id}/sessions")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await AppRevocationReasonsAsync(app.AppId)).Should().Equal("administrator");
    }

    [Fact]
    public async Task AppsAreListedApart_RevokedOneByOne_AndReconnectingReplaces()
    {
        var (user, _, browser) = await UserAsync();
        var first = await _kit.ConnectAsync(browser, scopes: ["printers.read", "production-runs.write", "print-jobs.cancel"]);

        var own = await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/apps");
        own.GetProperty("mcpUrl").GetString().Should().Be("https://farm.test/mcp");
        own.GetProperty("signInEnabled").GetBoolean().Should().BeTrue();
        var listed = own.GetProperty("apps").EnumerateArray().Should().ContainSingle().Subject;
        listed.GetProperty("id").GetGuid().Should().Be(first.AppId);
        listed.GetProperty("clientName").GetString().Should().Be("Test app");
        listed.GetProperty("clientIdSuffix").GetString().Should().Be(first.Client.Id[^4..]);
        listed.GetProperty("returnsTo").GetProperty("label").GetString().Should().Be("this computer (localhost)");
        using (var rowScope = factory.Services.CreateScope())
            (await rowScope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().BrowserSessions.AsNoTracking().SingleAsync(s => s.Id == first.AppId))
                .RedirectUri.Should().Be(OAuthKit.Loopback);
        var chips = listed.GetProperty("access").EnumerateArray().ToArray();
        chips.Select(c => c.GetProperty("area").GetString()).Should().Equal("Printers", "Runs", "Queue");
        chips[1].GetProperty("level").GetString().Should().Be("Change");
        chips[2].GetProperty("level").GetString().Should().Be("Read");
        chips[2].GetProperty("extras").EnumerateArray().Select(e => e.GetString()).Should().Equal("Cancel");
        chips[2].GetProperty("control").GetBoolean().Should().BeTrue();
        (await browser.GetFromJsonAsync<SessionResponse[]>("/api/v1/auth/sessions"))!.Should().OnlyContain(s => s.Id != first.AppId);

        var admin = await _kit.BrowserAsync();
        var adminList = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{user.Id}/apps");
        adminList.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().Equal(first.AppId);
        (await admin.GetAsync($"/api/v1/admin/users/{Guid.NewGuid()}/apps")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var accounts = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users");
        accounts.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == user.Id).GetProperty("sessionCount").GetInt32().Should().Be(1);

        // Reconnecting the same client replaces the older connection.
        var second = await _kit.ConnectAsync(browser, client: first.Client);
        (await _kit.McpStatusAsync(first.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await AppRevocationReasonsAsync(first.AppId)).Should().Equal("replaced");

        // Someone else's app id is a no-op; the owner's revoke works at once.
        (await admin.DeleteAsync($"/api/v1/auth/sessions/{second.AppId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _kit.McpStatusAsync(second.Access)).Should().Be(HttpStatusCode.OK);
        (await browser.DeleteAsync($"/api/v1/auth/sessions/{second.AppId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _kit.McpStatusAsync(second.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await AppRevocationReasonsAsync(second.AppId)).Should().Equal("user");

        var third = await _kit.ConnectAsync(browser);
        (await admin.DeleteAsync($"/api/v1/admin/users/{user.Id}/sessions/{third.AppId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _kit.McpStatusAsync(third.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/apps")).GetProperty("apps").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AnExchangeReplacesOnlyConnectedApps_NotAParallelPendingOne()
    {
        var (_, _, browser) = await UserAsync();
        var client = await _kit.ClientAsync();
        var (firstVerifier, firstChallenge) = OAuthKit.Pkce();
        var (secondVerifier, secondChallenge) = OAuthKit.Pkce();
        var firstRedirect = await OAuthKit.ApproveAsync(browser, await _kit.RequestAsync(client, firstChallenge), "printers.read");
        var secondRedirect = await OAuthKit.ApproveAsync(browser, await _kit.RequestAsync(client, secondChallenge), "printers.read");

        var (first, _, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(client, OAuthKit.CodeForm(client, firstRedirect, firstVerifier)));
        var (second, _, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(client, OAuthKit.CodeForm(client, secondRedirect, secondVerifier)));
        (await _kit.McpStatusAsync(second)).Should().Be(HttpStatusCode.OK);
        (await _kit.McpStatusAsync(first)).Should().Be(HttpStatusCode.Unauthorized);
        (await AppRevocationReasonsAsync(OAuthKit.SessionId(first))).Should().Equal("replaced");
        (await AppRevocationReasonsAsync(OAuthKit.SessionId(second))).Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeEndpoint_EndsTheAppByEitherToken_ForItsOwnClientOnly()
    {
        var app = await _kit.ConnectAsync();
        var other = await _kit.ClientAsync();
        using (var ignored = await _kit.PostAsync(other, new() { ["token"] = app.Access }, "/oauth/revoke"))
            ignored.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.OK);

        using (var unknown = await _kit.PostAsync(app.Client, new() { ["token"] = "nonsense" }, "/oauth/revoke"))
        {
            unknown.StatusCode.Should().Be(HttpStatusCode.OK);
            (await unknown.Content.ReadAsStringAsync()).Should().BeEmpty();
            unknown.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, [], "/oauth/revoke"))).Should().Be("invalid_request");
        // An empty value counts as omitted (RFC 6749 §3.1).
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, new() { ["token"] = "" }, "/oauth/revoke"))).Should().Be("invalid_request");
        using (var anonymous = await _kit.Raw().PostAsync("/oauth/revoke", new FormUrlEncodedContent([new("token", app.Access)])))
            (await OAuthKit.ErrorAsync(anonymous)).Should().Be("invalid_client");

        (await _kit.PostAsync(app.Client, new() { ["token"] = app.Access }, "/oauth/revoke")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh)))).Should().Be("invalid_grant");
        (await AppRevocationReasonsAsync(app.AppId)).Should().Equal("app");

        var byRefresh = await _kit.ConnectAsync();
        (await _kit.PostAsync(byRefresh.Client, new() { ["token"] = byRefresh.Refresh }, "/oauth/revoke")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _kit.McpStatusAsync(byRefresh.Access)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EachCredentialReachesOnlyItsSurface()
    {
        var browser = await _kit.BrowserAsync();
        var browserJwt = browser.DefaultRequestHeaders.Authorization!.Parameter!;
        var app = await _kit.ConnectAsync(browser);

        // Browser JWT: never /mcp.
        (await _kit.McpStatusAsync(browserJwt)).Should().Be(HttpStatusCode.Unauthorized);
        // App JWT: never REST or the hub, whatever the scheme casing.
        foreach (var scheme in new[] { "Bearer", "bearer", "BEARER" })
        {
            var asApp = factory.CreateClient();
            asApp.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"{scheme} {app.Access}");
            (await asApp.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, scheme);
            (await asApp.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, scheme);
            (await _kit.McpStatusAsync(app.Access, scheme)).Should().Be(HttpStatusCode.OK, scheme);
        }
        (await factory.CreateClient().PostAsync($"/hubs/farm/negotiate?negotiateVersion=1&access_token={app.Access}", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // The refresh credential is never a bearer.
        (await _kit.McpStatusAsync(app.Refresh)).Should().Be(HttpStatusCode.Unauthorized);
        var refreshBearer = factory.CreateClient();
        refreshBearer.DefaultRequestHeaders.Authorization = new("Bearer", app.Refresh);
        (await refreshBearer.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // An app session id inside a browser-audience JWT still fails: the browser check requires a browser session.
        using var scope = factory.Services.CreateScope();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Jwt:SigningKey"]!));
        var sub = new JwtSecurityTokenHandler().ReadJwtToken(app.Access).Subject;
        var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("OpenFarm.Tests", "OpenFarm.Tests",
            [new Claim("sub", sub), new Claim("sid", app.AppId.ToString())], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
        var asForged = factory.CreateClient();
        asForged.DefaultRequestHeaders.Authorization = new("Bearer", forged);
        (await asForged.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // And a browser session id inside an app-audience JWT fails on /mcp.
        var browserSid = new JwtSecurityTokenHandler().ReadJwtToken(browserJwt).Claims.Single(c => c.Type == "sid").Value;
        var forgedApp = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(OAuthFactory.Origin, OAuthFactory.Resource,
            [new Claim("sub", sub), new Claim("sid", browserSid), new Claim("client_id", app.Client.Id)], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
        (await _kit.McpStatusAsync(forgedApp)).Should().Be(HttpStatusCode.Unauthorized);
        // And an app session presented under another client id fails too.
        var otherClient = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(OAuthFactory.Origin, OAuthFactory.Resource,
            [new Claim("sub", sub), new Claim("sid", app.AppId.ToString()), new Claim("client_id", "ofc_" + new string('0', 32))], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
        (await _kit.McpStatusAsync(otherClient)).Should().Be(HttpStatusCode.Unauthorized);

        // A PAT still works on /mcp.
        var pat = await McpTestClient.TokenAsync(browser, "printers:list");
        (await _kit.McpStatusAsync(pat.Credential)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PendingAppsAreHidden_AndCleanedUpAfterTenMinutes()
    {
        var (_, _, browser) = await UserAsync();
        var client = await _kit.ClientAsync();
        var redirect = await OAuthKit.ApproveAsync(browser, await _kit.RequestAsync(client, OAuthKit.Pkce().Challenge), "printers.read");
        redirect.Should().NotBeNull();
        (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/apps")).GetProperty("apps").GetArrayLength().Should().Be(0);
        (await browser.GetFromJsonAsync<SessionResponse[]>("/api/v1/auth/sessions"))!.Should().ContainSingle();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await ActivatorUtilities.CreateInstance<OAuthService>(scope.ServiceProvider, new FutureTime(TimeSpan.FromMinutes(9))).CleanupAsync(CancellationToken.None);
        (await db.BrowserSessions.CountAsync(s => s.ClientId == client.Id)).Should().Be(1);
        await ActivatorUtilities.CreateInstance<OAuthService>(scope.ServiceProvider, new FutureTime(TimeSpan.FromMinutes(11))).CleanupAsync(CancellationToken.None);
        (await db.BrowserSessions.CountAsync(s => s.ClientId == client.Id)).Should().Be(0);

        // A client nobody connected goes after a day; one with a live app stays.
        var connected = await _kit.ConnectAsync(browser);
        await ActivatorUtilities.CreateInstance<OAuthService>(scope.ServiceProvider, new FutureTime(TimeSpan.FromHours(25))).CleanupAsync(CancellationToken.None);
        (await db.OAuthClients.AnyAsync(c => c.Id == client.Id)).Should().BeFalse();
        (await db.OAuthClients.AnyAsync(c => c.Id == connected.Client.Id)).Should().BeTrue();
    }
}
