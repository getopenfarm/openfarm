using System.Buffers.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.OAuth;

/// <summary>OAuth on for <see cref="Origin"/>, limits raised so the shared instance is never rate limited.</summary>
public sealed class OAuthFactory : ProductionTestFactory
{
    public const string Origin = "https://farm.test";
    public const string Resource = Origin + "/mcp";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ORIGIN", Origin);
        builder.UseSetting("OAUTH_ENABLED", "true");
        builder.UseSetting("OAuth:RegisterPermitLimit", "10000");
        builder.UseSetting("OAuth:PermitLimit", "10000");
    }
}

/// <summary>One Postgres container for every OAuth class; they run serially and share state.</summary>
[CollectionDefinition("OAuth")]
public sealed class OAuthGroup : ICollectionFixture<OAuthFactory>;

public sealed record OAuthApp(string Id, string? Secret, string Method, string Redirect);

/// <param name="AppId">The connected app's session id (the access token's <c>sid</c>).</param>
public sealed record Connected(OAuthApp Client, string Access, string Refresh, Guid AppId, HttpClient Browser);

/// <summary>Drives the protocol the way a client and a signed-in browser would.</summary>
public sealed class OAuthKit(WebApplicationFactory<Program> factory)
{
    public const string Loopback = "http://127.0.0.1:9123/cb";

    public HttpClient Raw() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri(OAuthFactory.Origin) });

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public Task<HttpResponseMessage> RegisterRawAsync(object body) => Raw().PostAsJsonAsync("/oauth/register", body);

    public async Task<OAuthApp> ClientAsync(string method = "none", string redirect = Loopback, string? name = "Test app")
    {
        using var response = await RegisterRawAsync(new { redirect_uris = new[] { redirect }, token_endpoint_auth_method = method, client_name = name });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var json = JsonDocument.Parse(body).RootElement;
        return new(json.GetProperty("client_id").GetString()!,
            json.TryGetProperty("client_secret", out var secret) ? secret.GetString() : null, method, redirect);
    }

    public static Dictionary<string, string?> AuthorizeQuery(OAuthApp client, string challenge, string? state = "xyz", string? redirect = null) => new()
    {
        ["response_type"] = "code",
        ["client_id"] = client.Id,
        ["redirect_uri"] = redirect ?? client.Redirect,
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = "S256",
        ["state"] = state,
        ["resource"] = OAuthFactory.Resource,
        ["scope"] = "printers.read openid",
    };

    public Task<HttpResponseMessage> AuthorizeAsync(IEnumerable<KeyValuePair<string, string?>> query) =>
        Raw().GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", query.Where(p => p.Value is not null)));

    /// <summary>The consent request value from a successful authorize redirect.</summary>
    public async Task<string> RequestAsync(OAuthApp client, string challenge, string? redirect = null, string? state = "xyz")
    {
        using var response = await AuthorizeAsync(AuthorizeQuery(client, challenge, state, redirect));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.OriginalString;
        location.Should().StartWith("/connect?request=");
        return QueryHelpers.ParseQuery(location[location.IndexOf('?', StringComparison.Ordinal)..])["request"]!;
    }

    public async Task<HttpClient> BrowserAsync(string username = "admin", string password = "admin-openfarm")
    {
        var client = factory.CreateClient();
        // Logins are throttled per address; each browser gets its own.
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}");
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(username, password));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        return client;
    }

    public static Task<HttpResponseMessage> DecideAsync(HttpClient browser, string request, bool approve, params string[] scopes) =>
        browser.PostAsJsonAsync("/api/v1/auth/oauth/consent", new ConsentDecisionRequest(request, approve, scopes));

    public static async Task<Uri> ApproveAsync(HttpClient browser, string request, params string[] scopes)
    {
        using var response = await DecideAsync(browser, request, true, scopes);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return new Uri((await response.Content.ReadFromJsonAsync<ConsentDecisionResponse>())!.RedirectUri);
    }

    public static string Param(Uri redirect, string name) => QueryHelpers.ParseQuery(redirect.Query)[name].ToString();

    /// <summary>POSTs a form to the token or revoke endpoint, authenticating the client the way it registered.</summary>
    public async Task<HttpResponseMessage> PostAsync(OAuthApp client, Dictionary<string, string> form, string path = "/oauth/token")
    {
        var body = new Dictionary<string, string>(form);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        switch (client.Method)
        {
            case "client_secret_basic":
                request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Id}:{client.Secret}")));
                break;
            case "client_secret_post":
                body.TryAdd("client_id", client.Id);
                body.TryAdd("client_secret", client.Secret!);
                break;
            default:
                body.TryAdd("client_id", client.Id);
                break;
        }
        request.Content = new FormUrlEncodedContent(body);
        return await Raw().SendAsync(request);
    }

    public static Dictionary<string, string> CodeForm(OAuthApp client, Uri redirect, string verifier) => new()
    {
        ["grant_type"] = "authorization_code",
        ["code"] = Param(redirect, "code"),
        ["redirect_uri"] = client.Redirect,
        ["code_verifier"] = verifier,
        ["resource"] = OAuthFactory.Resource,
    };

    public static Dictionary<string, string> RefreshForm(string refresh) => new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh };

    public static async Task<(string Access, string Refresh, string Scope)> TokensAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("token_type").GetString().Should().Be("Bearer");
        json.GetProperty("expires_in").GetInt32().Should().Be(900);
        return (json.GetProperty("access_token").GetString()!, json.GetProperty("refresh_token").GetString()!, json.GetProperty("scope").GetString()!);
    }

    /// <summary>Registers, authorizes, consents and exchanges: a connected app with its first tokens.</summary>
    public async Task<Connected> ConnectAsync(HttpClient? browser = null, string method = "none", string[]? scopes = null, OAuthApp? client = null)
    {
        browser ??= await BrowserAsync();
        client ??= await ClientAsync(method);
        var (verifier, challenge) = Pkce();
        var redirect = await ApproveAsync(browser, await RequestAsync(client, challenge), scopes ?? ["printers.read"]);
        using var exchanged = await PostAsync(client, CodeForm(client, redirect, verifier));
        var (access, refresh, _) = await TokensAsync(exchanged);
        return new(client, access, refresh, SessionId(access), browser);
    }

    public static Guid SessionId(string jwt) => Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Single(c => c.Type == "sid").Value);

    /// <summary>The status of a raw MCP <c>tools/list</c> with this bearer.</summary>
    public async Task<HttpStatusCode> McpStatusAsync(string bearer, string scheme = "Bearer")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, bearer);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        using var response = await Raw().SendAsync(request);
        return response.StatusCode;
    }

    public static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should().Equal(["error"], body);
        return json.GetProperty("error").GetString()!;
    }
}
