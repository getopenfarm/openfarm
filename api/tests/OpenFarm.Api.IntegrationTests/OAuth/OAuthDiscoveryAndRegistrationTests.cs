using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Auth.OAuth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.OAuth;

[Collection("OAuth")]
public sealed class OAuthDiscoveryAndRegistrationTests(OAuthFactory factory)
{
    private readonly OAuthKit _kit = new(factory);

    private const string Scopes = """["printers.read","print-jobs.read","production-runs.read","library.read","filament.read","maintenance.read","print-jobs.write","maintenance.write","print-jobs.pause","print-jobs.cancel"]""";

    private static string Compact(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

    [Fact]
    public async Task Discovery_IsExact_AndNamesOnlyTheConfiguredOrigin()
    {
        var resource = Compact($$"""
            {"resource":"https://farm.test/mcp","authorization_servers":["https://farm.test"],"scopes_supported":{{Scopes}},
             "bearer_methods_supported":["header"],"resource_name":"OpenFarm"}
            """);
        var server = Compact($$"""
            {"issuer":"https://farm.test","authorization_endpoint":"https://farm.test/oauth/authorize","token_endpoint":"https://farm.test/oauth/token",
             "registration_endpoint":"https://farm.test/oauth/register","revocation_endpoint":"https://farm.test/oauth/revoke",
             "response_types_supported":["code"],"response_modes_supported":["query"],"grant_types_supported":["authorization_code","refresh_token"],
             "token_endpoint_auth_methods_supported":["none","client_secret_basic","client_secret_post"],
             "revocation_endpoint_auth_methods_supported":["none","client_secret_basic","client_secret_post"],
             "code_challenge_methods_supported":["S256"],"authorization_response_iss_parameter_supported":true,"scopes_supported":{{Scopes}}}
            """);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "attacker.example";
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "evil.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        foreach (var (path, expected) in new[]
        {
            ("/.well-known/oauth-protected-resource/mcp", resource),
            ("/.well-known/oauth-protected-resource", resource),
            ("/.well-known/oauth-authorization-server", server),
        })
        {
            using var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
            Compact(await response.Content.ReadAsStringAsync()).Should().Be(expected, path);
            ProtocolHeaders(response);
        }
    }

    [Fact]
    public async Task UnknownProtocolPathsAreNotTheAppShell_AndTheContractHasNoProtocolRoutes()
    {
        foreach (var path in new[] { "/oauth/nope", "/.well-known/nope", "/.well-known/openid-configuration" })
        {
            using var response = await factory.CreateClient().GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html", path);
        }
        var openApi = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        openApi.Should().NotContain("\"/oauth").And.NotContain("\"/.well-known").And.NotContain("\"/mcp\"");
    }

    [Fact]
    public async Task McpChallengeNamesTheResourceMetadata()
    {
        using var anonymous = await factory.CreateClient().PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.Headers.WwwAuthenticate.ToString().Should().Be(
            "Bearer realm=\"openfarm-mcp\", resource_metadata=\"https://farm.test/.well-known/oauth-protected-resource/mcp\"");
        (await _kit.McpStatusAsync("nonsense")).Should().Be(HttpStatusCode.Unauthorized);
        var withHeader = factory.CreateClient();
        withHeader.DefaultRequestHeaders.Authorization = new("Bearer", "nonsense");
        using var invalid = await withHeader.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        invalid.Headers.WwwAuthenticate.ToString().Should().Be(
            "Bearer realm=\"openfarm-mcp\", resource_metadata=\"https://farm.test/.well-known/oauth-protected-resource/mcp\", error=\"invalid_token\"");
        // RFC 6750 §3.1: another scheme is not an invalid bearer token.
        var basic = factory.CreateClient();
        basic.DefaultRequestHeaders.Authorization = new("Basic", "dXNlcjpwYXNz");
        using var notBearer = await basic.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        notBearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        notBearer.Headers.WwwAuthenticate.ToString().Should().Be(
            "Bearer realm=\"openfarm-mcp\", resource_metadata=\"https://farm.test/.well-known/oauth-protected-resource/mcp\"");
    }

    [Fact]
    public async Task PostOnlyProtocolPathsRefuseOtherMethods()
    {
        foreach (var path in new[] { "/oauth/register", "/oauth/token", "/oauth/revoke" })
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
            {
                using var response = await _kit.Raw().SendAsync(new HttpRequestMessage(method, path));
                response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, $"{method} {path}");
                response.Content.Headers.Allow.Should().Equal("POST");
                (await OAuthKit.ErrorAsync(response)).Should().Be("invalid_request");
            }
    }

    [Fact]
    public async Task DisabledMode_AnswersEveryProtocolPathWith404Json_AndAuthorizeWithTheDisabledPage()
    {
        await using var off = factory.WithWebHostBuilder(b => b.UseSetting("OAUTH_ENABLED", "false"));
        var kit = new OAuthKit(off);
        foreach (var path in new[] { "/.well-known/oauth-protected-resource/mcp", "/.well-known/oauth-protected-resource", "/.well-known/oauth-authorization-server" })
        {
            using var response = await kit.Raw().GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"OAuth is disabled"}""");
        }
        foreach (var path in new[] { "/oauth/register", "/oauth/token", "/oauth/revoke" })
        {
            using var response = await kit.Raw().PostAsync(path, new FormUrlEncodedContent([]));
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"OAuth is disabled"}""");
        }
        using var authorize = await kit.Raw().GetAsync("/oauth/authorize?response_type=code");
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authorize.Headers.Location!.OriginalString.Should().Be("/connect/error?code=disabled");
        using var anonymous = await kit.Raw().PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        anonymous.Headers.WwwAuthenticate.ToString().Should().Be("Bearer realm=\"openfarm-mcp\"");

        var browser = await kit.BrowserAsync();
        var apps = await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/apps");
        apps.GetProperty("signInEnabled").GetBoolean().Should().BeFalse();
        (await browser.GetAsync("/api/v1/auth/oauth/consent?request=x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Registration_ReturnsTheClientOnce_AndStoresOnlyASecretHash()
    {
        using var response = await _kit.Raw().PostAsync("/oauth/register", new StringContent("""
            {"redirect_uris":["https://claude.ai/api/mcp/auth_callback"],"client_name":"  Claude  ","scope":"openid printers.read",
             "grant_types":["authorization_code","refresh_token"],"response_types":["code"],"logo_uri":"https://evil.example/logo.png",
             "application_type":"web"}
            """, Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        ProtocolHeaders(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("client_id").GetString()!;
        id.Should().MatchRegex("^ofc_[0-9a-f]{32}$");
        json.GetProperty("client_name").GetString().Should().Be("Claude");
        json.GetProperty("token_endpoint_auth_method").GetString().Should().Be("client_secret_basic");
        var secret = json.GetProperty("client_secret").GetString()!;
        secret.Should().MatchRegex("^ofs_[0-9a-f]{64}$");
        json.GetProperty("client_secret_expires_at").GetInt64().Should().Be(0);
        json.GetProperty("scope").GetString()!.Split(' ').Should().HaveCount(10);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var row = await db.OAuthClients.AsNoTracking().SingleAsync(c => c.Id == id);
        row.SecretHash.Should().Be(BrowserSessionService.Hash(secret));
        (await db.ActivityLogEntries.AnyAsync(a => a.EntityId == id)).Should().BeFalse();

        var none = await _kit.ClientAsync("none");
        none.Secret.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"redirect_uris":[]}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":"https://a.test/cb"}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":["http://farm.local/cb"]}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":["https://a.test/cb#x"]}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":["javascript:alert(1)"]}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":["com.example.app:/cb"]}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":[1]}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":["https://a.test/1","https://a.test/2","https://a.test/3","https://a.test/4","https://a.test/5","https://a.test/6","https://a.test/7","https://a.test/8","https://a.test/9","https://a.test/10","https://a.test/11"]}""", "invalid_redirect_uri")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"token_endpoint_auth_method":"private_key_jwt"}""", "invalid_client_metadata")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"grant_types":["implicit"]}""", "invalid_client_metadata")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"grant_types":[]}""", "invalid_client_metadata")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"response_types":["token"]}""", "invalid_client_metadata")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"scope":["printers.read"]}""", "invalid_client_metadata")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"client_name":"Claude‮edoc"}""", "invalid_client_metadata")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"client_name":7}""", "invalid_client_metadata")]
    [InlineData("""{"redirect_uris":["https://a.test/cb"],"redirect_uris":["https://b.test/cb"]}""", "invalid_client_metadata")]
    [InlineData("""[]""", "invalid_client_metadata")]
    [InlineData("""not json""", "invalid_client_metadata")]
    public async Task Registration_RefusesInvalidMetadata(string body, string error)
    {
        using var response = await _kit.Raw().PostAsync("/oauth/register", new StringContent(body, Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OAuthKit.ErrorAsync(response)).Should().Be(error);
    }

    [Fact]
    public async Task Registration_RefusesOtherContentTypesAndOversizeBodies()
    {
        using var form = await _kit.Raw().PostAsync("/oauth/register", new FormUrlEncodedContent([new("redirect_uris", "https://a.test/cb")]));
        (await OAuthKit.ErrorAsync(form)).Should().Be("invalid_request");
        var oversize = """{"redirect_uris":["https://a.test/cb"],"client_name":"x","pad":""" + "\"" + new string('a', 17_000) + "\"}";
        using var large = await _kit.Raw().PostAsync("/oauth/register", new StringContent(oversize, Encoding.UTF8, "application/json"));
        large.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await OAuthKit.ErrorAsync(large)).Should().Be("invalid_request");
        using var token = await _kit.Raw().PostAsync("/oauth/token", new StringContent("grant_type=" + new string('a', 17_000), Encoding.UTF8, "application/x-www-form-urlencoded"));
        token.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task AtCapacity_TheOldestClientNobodyConnectedIsReclaimed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
        var origin = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var missing = OAuthService.MaxClients - await db.OAuthClients.CountAsync();
        var seeded = Enumerable.Range(0, missing).Select(i => new OAuthClient
        {
            Id = $"ofc_seed{i:D28}",
            Name = "Seed",
            RedirectUris = ["https://seed.test/cb"],
            AuthMethod = "none",
            // Two old clients, the rest younger than a consent request.
            CreatedAtUtc = i < 2 ? origin.AddSeconds(i) : DateTimeOffset.UtcNow.AddMinutes(-1),
        }).ToList();
        db.OAuthClients.AddRange(seeded);
        // The oldest client has a connected user, so it is kept.
        db.BrowserSessions.Add(new BrowserSession
        {
            Id = Guid.NewGuid(),
            UserId = admin.Id,
            ClientId = seeded[0].Id,
            Scopes = ["printers.read"],
            Device = "Seed",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        });
        await db.SaveChangesAsync();
        try
        {
            await _kit.ClientAsync();
            (await db.OAuthClients.CountAsync()).Should().Be(OAuthService.MaxClients);
            (await db.OAuthClients.AnyAsync(c => c.Id == seeded[0].Id)).Should().BeTrue();
            (await db.OAuthClients.AnyAsync(c => c.Id == seeded[1].Id)).Should().BeFalse();
            // Nothing old and unconnected is left: a client someone may be connecting right now is never reclaimed.
            using var full = await _kit.RegisterRawAsync(new { redirect_uris = new[] { OAuthKit.Loopback } });
            full.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await OAuthKit.ErrorAsync(full)).Should().Be("temporarily_unavailable");
        }
        finally
        {
            await db.OAuthClients.Where(c => c.Name == "Seed" || c.Name == "Test app").ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task DefaultLimits_RefuseTheEleventhRegistrationInAMinute()
    {
        await using var limited = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("OAuth:RegisterPermitLimit", "10");
            b.UseSetting("OAuth:PermitLimit", "60");
        });
        var kit = new OAuthKit(limited);
        for (var i = 0; i < 10; i++) await kit.ClientAsync();
        using var refused = await kit.RegisterRawAsync(new { redirect_uris = new[] { OAuthKit.Loopback } });
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter!.ToString().Should().Be("60");
        (await OAuthKit.ErrorAsync(refused)).Should().Be("temporarily_unavailable");
        for (var i = 0; i < 60; i++) (await kit.Raw().GetAsync("/oauth/authorize")).Dispose();
        using var page = await kit.Raw().GetAsync("/oauth/authorize");
        page.Headers.Location!.OriginalString.Should().Be("/connect/error?code=temporarily_unavailable");
    }

    [Fact]
    public async Task Authorize_SendsEveryFailureToOpenFarmsErrorPage_NeverToTheClient()
    {
        var client = await _kit.ClientAsync(redirect: "https://client.example/cb");
        var (_, challenge) = OAuthKit.Pkce();
        var good = OAuthKit.AuthorizeQuery(client, challenge);
        var cases = new (string Error, Action<Dictionary<string, string?>> Change)[]
        {
            ("invalid_client", q => q["client_id"] = "ofc_00000000000000000000000000000000"),
            ("invalid_request", q => q["redirect_uri"] = "https://evil.example/cb"),
            ("invalid_request", q => q["redirect_uri"] = "https://client.example/cb/"),
            ("invalid_request", q => q["redirect_uri"] = null),
            ("invalid_request", q => q["code_challenge_method"] = "plain"),
            ("invalid_request", q => q["code_challenge"] = ""),
            ("invalid_request", q => q["code_challenge"] = null),
            ("invalid_request", q => q["state"] = "café"),
            ("invalid_request", q => q["response_mode"] = "fragment"),
            ("invalid_target", q => q["resource"] = "https://farm.test"),
            ("unsupported_response_type", q => q["response_type"] = "token"),
        };
        foreach (var (error, change) in cases)
        {
            var query = new Dictionary<string, string?>(good);
            change(query);
            using var response = await _kit.AuthorizeAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.OriginalString.Should().Be($"/connect/error?code={error}", error);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }

        using var duplicate = await _kit.Raw().GetAsync("/oauth/authorize?" + string.Join('&', good.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}")) + "&resource=https%3A%2F%2Fother.test");
        duplicate.Headers.Location!.OriginalString.Should().Be("/connect/error?code=invalid_request");
        using var huge = await _kit.Raw().GetAsync("/oauth/authorize?state=" + new string('a', 9000));
        huge.Headers.Location!.OriginalString.Should().Be("/connect/error?code=invalid_request");
    }

    [Fact]
    public async Task Authorize_AcceptsAnOmittedResource_AndAnyLoopbackPort()
    {
        var client = await _kit.ClientAsync(redirect: "http://127.0.0.1:1111/cb");
        var (_, challenge) = OAuthKit.Pkce();
        var query = OAuthKit.AuthorizeQuery(client, challenge, redirect: "http://127.0.0.1:2222/cb");
        query["resource"] = null;
        query["state"] = null;
        using var response = await _kit.AuthorizeAsync(query);
        response.Headers.Location!.OriginalString.Should().StartWith("/connect?request=");
        // resource= with no value is the same as no resource (RFC 6749 §3.1).
        query["resource"] = "";
        using var empty = await _kit.AuthorizeAsync(query);
        empty.Headers.Location!.OriginalString.Should().StartWith("/connect?request=");
    }

    private static void ProtocolHeaders(HttpResponseMessage response)
    {
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.Pragma.ToString().Should().Be("no-cache");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal("frame-ancestors 'none'");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("same-origin");
    }
}
