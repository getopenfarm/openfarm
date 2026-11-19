using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.IntegrationTests.Mcp;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Settings;

public sealed class AuthenticationSettingsTests(ProductionTestFactory factory) : IClassFixture<ProductionTestFactory>
{
    private const string Path = "/api/v1/admin/settings/authentication";
    private static Task<AuthenticationSettingsResponse?> ReadAsync(HttpClient browser) => browser.GetFromJsonAsync<AuthenticationSettingsResponse>(Path);
    private static AuthenticationSettingsValues Values(AuthenticationSettingsResponse saved) => new()
    {
        OidcEnabled = saved.OidcEnabled,
        InternalAuthenticationEnabled = saved.InternalAuthenticationEnabled,
        DisplayName = saved.DisplayName,
        Authority = saved.Authority,
        ClientId = saved.ClientId,
        SessionHours = saved.SessionHours,
        RecoveryUserIds = saved.RecoveryUserIds,
        JitEnabled = saved.JitEnabled,
        RequireVerifiedEmail = saved.RequireVerifiedEmail,
        DefaultGroup = saved.DefaultGroup,
    };

    [Fact]
    public async Task RuntimeSettings_AreEncryptedRedactedAndVisibleOnOtherHosts_WithoutRestart()
    {
        using var browser = await McpTestClient.BrowserAsync(factory);
        var server = (await browser.GetFromJsonAsync<ServerSettingsResponse>("/api/v1/admin/settings/server"))!;
        (await browser.PutAsJsonAsync("/api/v1/admin/settings/server", new { server.Revision, PublicUrl = "https://farm.example" })).EnsureSuccessStatusCode();
        var saved = (await ReadAsync(browser))!;
        await using var replica = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<OpenIdConnectOptions>(OpenFarm.Api.Security.OidcAuthentication.Scheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new()
                { Issuer = "https://identity.example/application/o/farm/", AuthorizationEndpoint = "https://identity.example/authorize", TokenEndpoint = "https://identity.example/token" }))));
        using var other = replica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await other.GetFromJsonAsync<LoginProvidersResponse>("/api/v1/auth/providers"))!.OidcEnabled.Should().BeFalse();
        const string credential = "client-credential-for-runtime-settings";
        var values = Values(saved) with
        {
            OidcEnabled = true,
            Authority = "https://identity.example/application/o/farm/",
            ClientId = "runtime-client",
            ClientSecret = credential,
            JitEnabled = true
        };
        using var response = await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, values));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain(credential);
        var changed = (await response.Content.ReadFromJsonAsync<AuthenticationSettingsResponse>())!;
        changed.ClientCredentialConfigured.Should().BeTrue();
        (await other.GetFromJsonAsync<LoginProvidersResponse>("/api/v1/auth/providers"))!.OidcEnabled.Should().BeTrue();
        using var challenge = await other.GetAsync("/api/v1/auth/sso/login");
        QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["client_id"].ToString().Should().Be("runtime-client");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var ciphertext = (await db.AuthenticationSettings.AsNoTracking().SingleAsync()).ProtectedConfiguration;
            ciphertext.Should().NotContain(credential);
            var settings = scope.ServiceProvider.GetRequiredService<AuthenticationConfiguration>();
            settings.Values.ClientSecret.Should().Be(credential);
            var audit = System.Text.Json.JsonSerializer.Serialize(await db.ActivityLogEntries.Where(e => e.EntityType == "AuthenticationSettingsRecord").ToArrayAsync());
            audit.Should().NotContain(credential).And.NotContain(ciphertext);
        }
        // Empty credential inputs mean retain. A stale editor cannot replace newer settings.
        var retained = Values(changed) with { DisplayName = "Farm identity" };
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(changed.Revision, retained))).EnsureSuccessStatusCode();
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(changed.Revision, retained))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var latest = (await ReadAsync(browser))!;
        var who = (await browser.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami"))!;
        Guid sid;
        using (var scope = factory.Services.CreateScope())
        {
            var session = await scope.ServiceProvider.GetRequiredService<BrowserSessionService>().CreateAsync(who.UserId, "SSO session", CancellationToken.None, external: true);
            sid = Guid.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims.Single(c => c.Type == "sid").Value);
        }
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(latest.Revision, Values(latest) with { ClientSecret = "rotated-client-credential" }))).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().BrowserSessions.AsNoTracking().SingleAsync(s => s.Id == sid)).RevokedAtUtc.Should().NotBeNull();
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        latest = (await ReadAsync(browser))!;
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(latest.Revision, Values(latest) with { OidcEnabled = false, JitEnabled = false }))).EnsureSuccessStatusCode();
        (await other.GetFromJsonAsync<LoginProvidersResponse>("/api/v1/auth/providers"))!.OidcEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task RecoveryAndProvisioningValidation_PreventLockoutAndProtectedRoleMapping()
    {
        using var browser = await McpTestClient.BrowserAsync(factory);
        var server = (await browser.GetFromJsonAsync<ServerSettingsResponse>("/api/v1/admin/settings/server"))!;
        (await browser.PutAsJsonAsync("/api/v1/admin/settings/server", new { server.Revision, PublicUrl = "https://farm.example" })).EnsureSuccessStatusCode();
        var saved = (await ReadAsync(browser))!;
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision,
            Values(saved) with { OidcEnabled = false, InternalAuthenticationEnabled = false }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var values = Values(saved) with { OidcEnabled = true, Authority = "https://identity.example/application/o/farm/", ClientId = "validation-client", ClientSecret = "validation-client-credential", InternalAuthenticationEnabled = false };
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, values))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        values = values with { RecoveryUserIds = [Guid.NewGuid()] };
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, values))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        values = values with { InternalAuthenticationEnabled = true, RecoveryUserIds = [], JitEnabled = true, DefaultGroup = "Administrators" };
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, values))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var who = (await browser.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami"))!;
        values = values with { InternalAuthenticationEnabled = false, RecoveryUserIds = [who.UserId], DefaultGroup = "Viewers" };
        // An untested provider cannot be used to disable internal authentication.
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, values))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var row = await db.AuthenticationSettings.SingleAsync();
            row.TestedProviderIdentity = new OidcSettings(values).TestIdentity("https://farm.example");
            await db.SaveChangesAsync();
        }
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, values))).EnsureSuccessStatusCode();
        (await browser.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        saved = (await ReadAsync(browser))!;
        saved.OidcTested.Should().BeTrue();
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision,
            Values(saved) with { ClientSecret = "rotated-untested-client-credential" }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, Values(saved) with { OidcEnabled = false, InternalAuthenticationEnabled = true, JitEnabled = false }))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StaleSnapshots_CannotAuthenticateOrMutateAccountsAfterAPolicyChange()
    {
        using var browser = await McpTestClient.BrowserAsync(factory);
        var saved = (await ReadAsync(browser))!;
        using var old = factory.Services.CreateScope();
        await old.ServiceProvider.GetRequiredService<AuthenticationConfiguration>().InitializeAsync(CancellationToken.None);
        var authentication = old.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var accounts = old.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        (await browser.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(saved.Revision, Values(saved) with { DisplayName = Guid.NewGuid().ToString("N") }))).EnsureSuccessStatusCode();
        (await authentication.AuthenticateAsync("admin", "admin-openfarm", CancellationToken.None)).Should().BeNull();
        await Assert.ThrowsAsync<ServerSettingsConflictException>(() => accounts.SaveGroupAsync(null,
            new GroupInput("stale-group", "", []), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Settings_RequireAnActualAdministratorBrowserSession()
    {
        using var browser = await McpTestClient.BrowserAsync(factory);
        var token = await McpTestClient.TokenAsync(browser, "printers:list");
        using var native = factory.CreateClient(); native.DefaultRequestHeaders.Authorization = new("Bearer", token.Credential);
        (await native.GetAsync(Path)).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var name = "sso-manager-" + Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = name, Email = name + "@example.test" };
        (await users.CreateAsync(user, "test-password")).Succeeded.Should().BeTrue();
        (await users.AddClaimAsync(user, new(PermissionCatalog.GrantClaim, "users:manage"))).Succeeded.Should().BeTrue();
        using var delegated = factory.CreateClient();
        var login = await delegated.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(name, "test-password")); login.EnsureSuccessStatusCode();
        delegated.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        (await delegated.GetAsync(Path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await delegated.PutAsJsonAsync(Path, new UpdateAuthenticationSettingsRequest(Guid.NewGuid(), new()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
