using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace OpenFarm.Api.IntegrationTests.OAuth;

/// <summary>Collects every Serilog event of a host configured with <c>Serilog:WriteTo:0:Name = Collecting</c>.</summary>
public static class CollectingSinkExtensions
{
    public static readonly ConcurrentQueue<LogEvent> Events = new();

    public static LoggerConfiguration Collecting(this LoggerSinkConfiguration sink) => sink.Sink(new Queue());

    private sealed class Queue : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}

/// <summary>The official MCP C# client signs in by itself: discovery, registration, PKCE, consent, code exchange, refresh.</summary>
[Collection("OAuth")]
public sealed class OAuthMcpEndToEndTests(OAuthFactory factory) : ProductionTestContext(factory)
{
    private sealed class RecordingCache : ITokenCache
    {
        public TokenContainer? Tokens { get; set; }
        public List<TokenContainer> Stored { get; } = [];

        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Tokens);

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            Tokens = tokens;
            Stored.Add(tokens);
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<AuthorizationResult> SignInAsync(OAuthKit kit, HttpClient browser, Uri authorizationUri, List<string> secrets, bool wrongIssuer)
    {
        using var authorize = await kit.Raw().GetAsync(authorizationUri.PathAndQuery);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = authorize.Headers.Location!.OriginalString;
        location.Should().StartWith("/connect?request=");
        var request = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location[location.IndexOf('?', StringComparison.Ordinal)..])["request"].ToString();
        secrets.Add(request);
        (await browser.GetAsync("/api/v1/auth/oauth/consent?request=" + Uri.EscapeDataString(request))).StatusCode.Should().Be(HttpStatusCode.OK);
        var redirect = await OAuthKit.ApproveAsync(browser, request, "printers.read", "print-jobs.read", "print-jobs.cancel");
        secrets.Add(OAuthKit.Param(redirect, "code"));
        return new AuthorizationResult
        {
            Code = OAuthKit.Param(redirect, "code"),
            State = OAuthKit.Param(redirect, "state"),
            Iss = wrongIssuer ? "https://evil.example" : OAuthKit.Param(redirect, "iss"),
        };
    }

    private static HttpClientTransport Transport(WebApplicationFactory<Program> app, RecordingCache cache,
        Func<AuthorizationCallbackContext, CancellationToken, Task<AuthorizationResult>> callback) =>
        new(new HttpClientTransportOptions
        {
            Endpoint = new Uri(OAuthFactory.Resource),
            TransportMode = HttpTransportMode.StreamableHttp,
            OAuth = new ClientOAuthOptions
            {
                RedirectUri = new Uri("http://127.0.0.1:0/cb"),
                DynamicClientRegistration = new DynamicClientRegistrationOptions { ClientName = "SDK e2e" },
                TokenCache = cache,
                AuthorizationCallbackHandler = async (context, ct) => await callback(context, ct),
            },
        }, app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(OAuthFactory.Origin) }), ownsHttpClient: true);

    [Fact]
    public async Task TheSdkClientConnects_CallsScopedTools_Refreshes_AndLosesAccessOnPasswordChange()
    {
        // The shared host first: Serilog's static logger belongs to the host built last, and this test reads its logs.
        var run = await RunAsync(await ClientAsync(), (await PartAsync()).Id);
        var jobId = (await JobsAsync(run.Id)).Single().Id;
        await using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Serilog:Using:0", typeof(CollectingSinkExtensions).Assembly.GetName().Name);
            b.UseSetting("Serilog:WriteTo:0", nameof(CollectingSinkExtensions.Collecting));
        });
        CollectingSinkExtensions.Events.Clear();
        var kit = new OAuthKit(app);

        // A dedicated administrator, so changing the password leaves the shared admin alone.
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "e2e-" + Guid.NewGuid().ToString("N")[..10], Email = $"{Guid.NewGuid():N}@example.test" };
        (await users.CreateAsync(user, "e2e-password")).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, "Administrators");
        var browser = await kit.BrowserAsync(user.UserName!, "e2e-password");

        var secrets = new List<string>();
        var callbacks = 0;
        var cache = new RecordingCache();
        await using (var client = await McpClient.CreateAsync(Transport(app, cache, (context, _) =>
        {
            callbacks++;
            context.AuthorizationUri.GetLeftPart(UriPartial.Path).Should().Be("https://farm.test/oauth/authorize");
            return SignInAsync(kit, browser, context.AuthorizationUri, secrets, wrongIssuer: false);
        })))
        {
            var tools = await client.ListToolsAsync();
            tools.Select(t => t.Name).Should().BeEquivalentTo("get_farm_overview", "list_printers", "get_printer", "list_print_jobs", "get_print_job", "cancel_print_job");

            var me = await client.CallToolAsync("get_farm_overview");
            me.StructuredContent!.Value.GetProperty("connection").GetString().Should().Be("App: SDK e2e");

            var cancelled = await client.CallToolAsync("cancel_print_job", new Dictionary<string, object?> { ["jobId"] = jobId, ["expectedPhase"] = "queued" });
            cancelled.IsError.Should().NotBe(true);
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var row = await db.ActivityLogEntries.AsNoTracking().SingleAsync(a => a.EntityId == jobId.ToString() && a.EventType == "PrintJobCancelRequested");
            row.Details!.Via!.Kind.Should().Be("App");
            row.Details.Via.Label.Should().Be("SDK e2e");
            row.Details.Via.CredentialId.Should().Be(OAuthKit.SessionId(cache.Tokens!.AccessToken));

            // A rejected access token makes the client refresh by itself, rotating the refresh credential.
            var first = cache.Tokens!;
            cache.Tokens = new TokenContainer
            {
                AccessToken = "spoiled",
                RefreshToken = first.RefreshToken,
                TokenType = first.TokenType,
                ExpiresIn = first.ExpiresIn,
                ObtainedAt = first.ObtainedAt,
                Scope = first.Scope,
                ClientId = first.ClientId,
                ClientSecret = first.ClientSecret,
                TokenEndpointAuthMethod = first.TokenEndpointAuthMethod,
                AuthorizationServer = first.AuthorizationServer,
            };
            (await client.ListToolsAsync()).Should().HaveCount(6);
            callbacks.Should().Be(1);
            cache.Tokens!.RefreshToken.Should().NotBe(first.RefreshToken);
            secrets.AddRange([first.AccessToken, first.RefreshToken!, cache.Tokens.AccessToken, cache.Tokens.RefreshToken!]);
            if (first.ClientSecret is { } secret) secrets.Add(secret);
        }

        (await browser.PostAsJsonAsync("/api/v1/auth/change-password", new PasswordInput("e2e-password", "e2e-changed"))).EnsureSuccessStatusCode();
        (await kit.McpStatusAsync(cache.Tokens!.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);

        // Nothing secret, and not the client's own name, reaches the logs.
        var logged = CollectingSinkExtensions.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Values) + " " + e.Exception).ToList();
        logged.Should().Contain(l => l.Contains("MCP", StringComparison.Ordinal));
        foreach (var value in secrets.Append("SDK e2e"))
            logged.Should().NotContain(l => l.Contains(value, StringComparison.Ordinal), value.Length > 12 ? value[..12] + "…" : value);
    }

    [Fact]
    public async Task AWrongIssuerInTheAuthorizationResponseIsRefusedByTheClient()
    {
        var kit = new OAuthKit(factory);
        var browser = await kit.BrowserAsync();
        var secrets = new List<string>();
        var connect = async () => await McpClient.CreateAsync(Transport(factory, new RecordingCache(),
            (context, _) => SignInAsync(kit, browser, context.AuthorizationUri, secrets, wrongIssuer: true)));
        await connect.Should().ThrowAsync<Exception>();
        secrets.Should().NotBeEmpty();
    }
}
