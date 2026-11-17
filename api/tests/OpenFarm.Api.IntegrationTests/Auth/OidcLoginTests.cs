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
        (await owner.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=failed");
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
            await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<OidcAccountService>()
                .CompleteLinkAsync(proof, credential, "replacement-" + userId, CancellationToken.None));
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
        (await other.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/profile?tab=security&sso=failed");
        (await other.GetFromJsonAsync<OidcIdentityResponse>("/api/v1/auth/sso/identity"))!.Linked.Should().BeFalse();
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsEnabled, false));
        using var browser = kit.CreateClient();
        callback = await kit.CallbackAsync(browser, "/api/v1/auth/sso/login", subject);
        (await browser.GetAsync(callback)).Headers.Location!.OriginalString.Should().Be("/login?ssoError=unavailable");
        (await owner.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HttpBackend_UsesConfiguredHttpsCallbackAndSecureCorrelationCookies()
    {
        await using var kit = new OidcKit(factory);
        using var browser = kit.App.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://internal:6868"), AllowAutoRedirect = false });
        var challenge = await browser.GetAsync("/api/v1/auth/sso/login");
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["redirect_uri"].ToString().Should().Be("https://farm.example" + OidcAuthentication.CallbackPath);
        challenge.Headers.GetValues("Set-Cookie").Should().OnlyContain(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase)
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
        var options = kit.App.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OidcAuthentication.Scheme);
        options.ConfigurationManager = new UnavailableConfigurationManager();
        var response = await browser.GetAsync("/api/v1/auth/sso/login");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/login?ssoError=failed");
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

        public OidcKit(CustomWebApplicationFactory parent, bool required = false, bool jit = false)
        {
            if (required)
            {
                using var scope = parent.Services.CreateScope();
                RecoveryId = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Users.Single(u => u.UserName == "admin").Id;
            }
            App = parent.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ORIGIN", "https://farm.example");
                builder.UseSetting("Auth:Oidc:Enabled", "true");
                builder.UseSetting("Auth:Oidc:Authority", FakeProvider.Issuer);
                builder.UseSetting("Auth:Oidc:ClientId", "farm");
                builder.UseSetting("Auth:Oidc:ClientSecret", "test-client-credential");
                if (jit) builder.UseSetting("Auth:Oidc:Jit:Enabled", "true");
                if (required)
                {
                    builder.UseSetting("Auth:Oidc:Required", "true");
                    builder.UseSetting("Auth:Oidc:RecoveryUserIds:0", RecoveryId.ToString());
                }
                builder.ConfigureServices(services => services.PostConfigure<OpenIdConnectOptions>(OidcAuthentication.Scheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = FakeProvider.Issuer,
                        AuthorizationEndpoint = FakeProvider.Issuer + "authorize",
                        TokenEndpoint = FakeProvider.Issuer + "token",
                    };
                    configuration.SigningKeys.Add(Provider.Key);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.Backchannel = new HttpClient(Provider);
                    var failure = options.Events.OnRemoteFailure;
                    options.Events.OnRemoteFailure = async context =>
                    {
                        Provider.LastFailure = context.Failure?.ToString();
                        await failure(context);
                    };
                }));
            });
        }

        public async Task<Guid> AuthenticateAsync(HttpClient client)
        {
            using var scope = App.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var username = "oidc-" + Guid.NewGuid().ToString("N");
            var user = new ApplicationUser { UserName = username, Email = username + "@example.com" };
            (await users.CreateAsync(user, "admin-openfarm")).Succeeded.Should().BeTrue();
            (await users.AddToRoleAsync(user, "Viewers")).Succeeded.Should().BeTrue();
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
        private readonly Dictionary<string, (string Subject, string Nonce, string Challenge, string? Invalid)> _codes = [];
        public int Exchanges { get; private set; }
        public string? LastFailure { get; set; }
        public ExternalProfile? Profile { get; set; }
        public FakeProvider() => Key = new(_rsa) { KeyId = "test-key" };

        public string Issue(string subject, string nonce, string challenge, string? invalid)
        {
            var code = Guid.NewGuid().ToString();
            _codes.Add(code, (subject, nonce, challenge, invalid));
            return code;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsoluteUri.Should().Be(Issuer + "token");
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            form["client_secret"].ToString().Should().Be("test-client-credential");
            form["redirect_uri"].ToString().Should().Be("https://farm.example" + OidcAuthentication.CallbackPath);
            if (!_codes.Remove(form["code"]!, out var proof)) return new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };
            Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!))).Should().Be(proof.Challenge);
            Exchanges++;
            var now = DateTime.UtcNow;
            var claims = new List<Claim> { new("sub", proof.Subject), new("nonce", proof.Invalid == "nonce" ? "wrong" : proof.Nonce),
                new("iat", EpochTime.GetIntDate(now).ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64) };
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
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
            { access_token = "external-access-token", token_type = "Bearer", expires_in = 300, id_token = encoded }) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _rsa.Dispose();
            base.Dispose(disposing);
        }
    }
}
