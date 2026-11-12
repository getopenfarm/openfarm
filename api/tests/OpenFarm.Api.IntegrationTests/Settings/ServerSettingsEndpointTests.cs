using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.IntegrationTests.Mcp;
using OpenFarm.Api.IntegrationTests.OAuth;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Settings;

public sealed class DefaultMcpSettingsTests(ProductionTestFactory factory) : IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task DefaultOff_BlocksValidTokensAndProtocolEndpoints_WhileRestStillWorks()
    {
        using var browser = await McpTestClient.BrowserAsync(factory);
        var saved = (await browser.GetFromJsonAsync<ServerSettingsResponse>("/api/v1/admin/settings/server"))!;
        saved.McpMode.Should().Be("Disabled");
        var token = await McpTestClient.TokenAsync(browser, "printers:list");
        using var native = factory.CreateClient();
        native.DefaultRequestHeaders.Authorization = new("Bearer", token.Credential);
        (await native.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var path in new[] { "/mcp", "/oauth/register", "/oauth/token", "/oauth/revoke", "/oauth/authorize", "/.well-known/oauth-protected-resource/mcp", "/.well-known/oauth-authorization-server" })
        {
            using var response = await native.PostAsJsonAsync(path, new { });
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
        var apps = (await browser.GetFromJsonAsync<ConnectedAppsResponse>("/api/v1/auth/apps"))!;
        apps.McpEnabled.Should().BeFalse();
        apps.SignInEnabled.Should().BeFalse();
    }
}

public sealed class ServerSettingsEndpointTests(ProductionTestFactory factory) : IClassFixture<ProductionTestFactory>
{
    private const string ServerPath = "/api/v1/admin/settings/server";
    private const string McpPath = "/api/v1/admin/settings/mcp";

    private static async Task<ServerSettingsResponse> ReadAsync(HttpClient browser) => (await browser.GetFromJsonAsync<ServerSettingsResponse>(ServerPath))!;

    private static async Task<ServerSettingsResponse> SaveAsync(HttpClient browser, string? mode = null, string? url = null)
    {
        var current = await ReadAsync(browser);
        using var response = mode is null
            ? await browser.PutAsJsonAsync(ServerPath, new { current.Revision, publicUrl = url, password = "admin-openfarm" })
            : await browser.PutAsJsonAsync(McpPath, new { current.Revision, mode, password = "admin-openfarm" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ServerSettingsResponse>())!;
    }

    private async Task<HttpClient> BrowserAsync()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        await SaveAsync(browser, mode: "Disabled");
        await SaveAsync(browser);
        return browser;
    }

    [Fact]
    public async Task TokenModeAndDisable_AffectTheNextCallOnAnotherHost_AndSurviveRestart()
    {
        using var browser = await BrowserAsync();
        var token = await McpTestClient.TokenAsync(browser, "printers:list");
        await SaveAsync(browser, mode: "AccessTokens");
        await using var replica = factory.WithWebHostBuilder(_ => { });
        var kit = new OAuthKit(replica);
        (await kit.McpStatusAsync(token.Credential)).Should().Be(HttpStatusCode.OK);
        using var replicaBrowser = await McpTestClient.BrowserAsync(replica);
        (await ReadAsync(replicaBrowser)).McpMode.Should().Be("AccessTokens");
        await SaveAsync(browser, mode: "Disabled");
        (await kit.McpStatusAsync(token.Credential)).Should().Be(HttpStatusCode.NotFound);
        using var rest = replica.CreateClient();
        rest.DefaultRequestHeaders.Authorization = new("Bearer", token.Credential);
        (await rest.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RuntimeOAuth_UrlChangesAndModeChangesInvalidateGrantsCodesAndRequests()
    {
        using var browser = await BrowserAsync();
        await SaveAsync(browser, url: OAuthFactory.Origin);
        await SaveAsync(browser, mode: "SignIn");
        var kit = new OAuthKit(factory);
        var app = await kit.ConnectAsync(browser);
        (await kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.OK);
        var (_, challenge) = OAuthKit.Pkce();
        var pendingRequest = await kit.RequestAsync(app.Client, challenge);
        var code = await OAuthKit.ApproveAsync(browser, await kit.RequestAsync(app.Client, challenge), "printers.read");

        await SaveAsync(browser, url: "https://other.test");
        using var raw = kit.Raw();
        var metadata = await raw.GetStringAsync("/.well-known/oauth-authorization-server");
        metadata.Should().Contain("https://other.test/oauth/token").And.NotContain(OAuthFactory.Origin);
        (await kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.DecideAsync(browser, pendingRequest, true, "printers.read")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var oldCode = await kit.PostAsync(app.Client, OAuthKit.CodeForm(app.Client, code, "wrong"));
        (await OAuthKit.ErrorAsync(oldCode)).Should().Be("invalid_target");
        using var refresh = await kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh));
        (await OAuthKit.ErrorAsync(refresh)).Should().Be("invalid_grant");

        // Going back to the same issuer does not resurrect old requests, codes or app grants.
        await SaveAsync(browser, url: OAuthFactory.Origin);
        (await kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.DecideAsync(browser, pendingRequest, true, "printers.read")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var burnedCode = await kit.PostAsync(app.Client, OAuthKit.CodeForm(app.Client, code, "wrong"));
        (await OAuthKit.ErrorAsync(burnedCode)).Should().Be("invalid_grant");
        var reconnected = await kit.ConnectAsync(browser);
        (await kit.McpStatusAsync(reconnected.Access)).Should().Be(HttpStatusCode.OK);
        await SaveAsync(browser, mode: "Disabled");
        (await kit.McpStatusAsync(reconnected.Access)).Should().Be(HttpStatusCode.NotFound);
        (await kit.RegisterRawAsync(new { redirect_uris = new[] { OAuthKit.Loopback } })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OAuthKit.DecideAsync(browser, pendingRequest, true, "printers.read")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await SaveAsync(browser, mode: "SignIn");
        (await kit.McpStatusAsync(reconnected.Access)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublicUrl_UpdatesCorsCookieSecurityAndBrowserOriginChecksTogether()
    {
        using var browser = await BrowserAsync();
        await SaveAsync(browser, url: "https://public.test");
        await SaveAsync(browser, mode: "AccessTokens");
        using var raw = factory.CreateClient();
        raw.DefaultRequestHeaders.Add("Origin", "https://public.test");
        raw.DefaultRequestHeaders.Add("X-Forwarded-For", "10.22.33.44");
        using var login = await raw.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("openfarm.refresh=", StringComparison.Ordinal) && c.Contains("secure", StringComparison.OrdinalIgnoreCase));
        login.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://public.test");
        raw.DefaultRequestHeaders.Remove("Origin");
        raw.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        (await raw.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await raw.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PasswordValidationAndRevisionChecks_PreventPartialChangesAndAuditNeverContainsPasswords()
    {
        using var browser = await BrowserAsync();
        var before = await ReadAsync(browser);
        (await browser.PutAsJsonAsync(McpPath, new { before.Revision, mode = "AccessTokens", password = "incorrect-secret" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await browser.PutAsJsonAsync(McpPath, new { before.Revision, mode = "SignIn", password = "admin-openfarm" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadAsync(browser)).Should().Be(before);
        foreach (var url in new[] { "https://user:secret@farm.test", "https://farm.test/path", "https://farm.test?x=1", "https://farm.test/app/.." })
            (await browser.PutAsJsonAsync(ServerPath, new { before.Revision, publicUrl = url, password = "admin-openfarm" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await SaveAsync(browser, mode: "AccessTokens");
        (await browser.PutAsJsonAsync(McpPath, new { before.Revision, mode = "Disabled", password = "admin-openfarm" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var audit = await db.ActivityLogEntries.Where(a => a.EntityType == "ServerSettings").ToArrayAsync();
        audit.Should().Contain(a => a.EventType == "McpModeChanged");
        JsonSerializer.Serialize(audit).Should().NotContain("admin-openfarm").And.NotContain("incorrect-secret");
    }

    [Fact]
    public async Task LegacyOptIn_PreservesExistingGrantsDuringTheOneTimeSettingsImport()
    {
        using var browser = await BrowserAsync();
        await SaveAsync(browser, url: OAuthFactory.Origin);
        await SaveAsync(browser, mode: "SignIn");
        var app = await new OAuthKit(factory).ConnectAsync(browser);
        // Simulate the pre-migration installation state: grants exist, installation settings do not.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            await db.ServerSettings.ExecuteDeleteAsync();
            await db.BrowserSessions.Where(s => s.ClientId != null).ExecuteUpdateAsync(s => s.SetProperty(x => x.OAuthConfigurationIdentity, (string?)null));
        }
        await using var upgraded = factory.WithWebHostBuilder(b => { b.UseSetting("ORIGIN", OAuthFactory.Origin); b.UseSetting("OAUTH_ENABLED", "true"); });
        var kit = new OAuthKit(upgraded);
        (await kit.McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.OK);
        using var refresh = await kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh));
        await OAuthKit.TokensAsync(refresh);
        using var admin = await McpTestClient.BrowserAsync(upgraded);
        await SaveAsync(admin, mode: "Disabled");
        // Legacy OAUTH_ENABLED cannot override an administrator's saved disable on later startup.
        await using var restarted = factory.WithWebHostBuilder(b => { b.UseSetting("ORIGIN", OAuthFactory.Origin); b.UseSetting("OAUTH_ENABLED", "true"); });
        (await new OAuthKit(restarted).McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeploymentUrlChanges_InvalidateGrantsEvenWhenTheOldUrlIsRestored()
    {
        using var browser = await BrowserAsync();
        await SaveAsync(browser, url: OAuthFactory.Origin);
        await SaveAsync(browser, mode: "SignIn");
        var app = await new OAuthKit(factory).ConnectAsync(browser);
        await using (var changed = factory.WithWebHostBuilder(b => b.UseSetting("ORIGIN", "https://changed.test")))
            (await new OAuthKit(changed).McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
        await using var restored = factory.WithWebHostBuilder(b => b.UseSetting("ORIGIN", ""));
        (await new OAuthKit(restored).McpStatusAsync(app.Access)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnabledTrueOverridesSavedDisable_AndReportsThePositiveDeploymentPolicy()
    {
        using var browser = await BrowserAsync();
        var token = await McpTestClient.TokenAsync(browser, "printers:list");
        await using var enabled = factory.WithWebHostBuilder(b => b.UseSetting("MCP_ENABLED", "true"));
        using var admin = await McpTestClient.BrowserAsync(enabled);
        var settings = await ReadAsync(admin);
        settings.McpEnabledOverride.Should().BeTrue();
        settings.McpModeManaged.Should().BeTrue();
        settings.McpMode.Should().Be("AccessTokens");
        (await new OAuthKit(enabled).McpStatusAsync(token.Credential)).Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync(McpPath, new { settings.Revision, mode = "Disabled", password = "admin-openfarm" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task OnlyAdministratorBrowsersCanManageSettings_DeploymentOverridesCannotBeBypassed()
    {
        using var browser = await BrowserAsync();
        var token = await McpTestClient.TokenAsync(browser, "printers:list");
        using var native = factory.CreateClient();
        native.DefaultRequestHeaders.Authorization = new("Bearer", token.Credential);
        (await native.GetAsync(ServerPath)).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        var name = "delegated-" + Guid.NewGuid().ToString("N");
        var groups = (await browser.GetFromJsonAsync<GroupResponse[]>("/api/v1/admin/groups"))!;
        using var created = await browser.PostAsJsonAsync("/api/v1/admin/users", new AccountInput(name, name + "@test.local", null,
            [groups.Single(g => g.Name == "Viewers").Id], ["settings:general:update"], true, "test-password"));
        created.EnsureSuccessStatusCode();
        using var delegated = factory.CreateClient();
        using var login = await delegated.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(name, "test-password"));
        delegated.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        (await delegated.GetAsync(ServerPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await delegated.PutAsJsonAsync(McpPath, new { revision = Guid.NewGuid(), mode = "SignIn", password = "test-password" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await SaveAsync(browser, mode: "AccessTokens");
        await using var locked = factory.WithWebHostBuilder(b => { b.UseSetting("MCP_ENABLED", "false"); b.UseSetting("ORIGIN", "https://managed.test"); });
        using var admin = await McpTestClient.BrowserAsync(locked);
        var managed = await ReadAsync(admin);
        managed.McpEnabledOverride.Should().BeFalse();
        managed.PublicUrlManaged.Should().BeTrue();
        managed.PublicUrl.Should().Be("https://managed.test");
        (await new OAuthKit(locked).McpStatusAsync(token.Credential)).Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync(McpPath, new { managed.Revision, mode = "AccessTokens", password = "admin-openfarm" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PutAsJsonAsync(ServerPath, new { managed.Revision, publicUrl = "https://bypass.test", password = "admin-openfarm" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
