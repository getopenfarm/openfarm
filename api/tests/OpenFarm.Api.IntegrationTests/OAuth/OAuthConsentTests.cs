using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.IntegrationTests.Mcp;
using OpenFarm.Infrastructure.Auth.OAuth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.OAuth;

[Collection("OAuth")]
public sealed class OAuthConsentTests(OAuthFactory factory)
{
    private readonly OAuthKit _kit = new(factory);

    private sealed record ConsentBody(string Request, bool Approve, string[] Scopes);

    /// <summary>Issues with a clock in the past, so the payloads it signs are already expired.</summary>
    private sealed class PastTime(TimeSpan ago) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() - ago;
    }

    [Fact]
    public async Task Describe_ShowsTheClientAndEveryScope_PreselectingOnlyReads()
    {
        var client = await _kit.ClientAsync(name: "Claude Code");
        var request = await _kit.RequestAsync(client, OAuthKit.Pkce().Challenge);
        var browser = await _kit.BrowserAsync();
        using var response = await browser.GetAsync("/api/v1/auth/oauth/consent?request=" + Uri.EscapeDataString(request));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("clientId").GetString().Should().Be(client.Id);
        json.GetProperty("clientName").GetString().Should().Be("Claude Code");
        json.GetProperty("returnsTo").GetProperty("kind").GetString().Should().Be("Loopback");
        json.GetProperty("returnsTo").GetProperty("label").GetString().Should().Be("this computer (localhost)");
        json.GetProperty("unverifiedName").GetBoolean().Should().BeTrue();
        json.GetProperty("instance").GetString().Should().Be("https://farm.test");
        json.GetProperty("username").GetString().Should().Be("admin");
        json.GetProperty("accessEndsAtUtc").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        var tiers = json.GetProperty("tiers").EnumerateArray().ToArray();
        tiers.Select(t => t.GetProperty("tier").GetString()).Should().Equal("Read", "Change", "PrinterControl");
        tiers[0].GetProperty("scopes").EnumerateArray().Should().OnlyContain(s => s.GetProperty("preselected").GetBoolean() && s.GetProperty("allowed").GetBoolean());
        tiers[1].GetProperty("scopes").EnumerateArray().Concat(tiers[2].GetProperty("scopes").EnumerateArray())
            .Should().OnlyContain(s => !s.GetProperty("preselected").GetBoolean());
        var printers = tiers[0].GetProperty("scopes")[0];
        printers.GetProperty("scope").GetString().Should().Be("printers.read");
        printers.GetProperty("label").GetString().Should().Be("Read printers, their live status and feed units");
        printers.GetProperty("includes").EnumerateArray().Select(e => e.GetString()).Should().Equal("Printers: List", "Printers: View");
    }

    [Fact]
    public async Task OnlyABrowserSessionCanSeeOrDecide()
    {
        var request = await _kit.RequestAsync(await _kit.ClientAsync(), OAuthKit.Pkce().Challenge);
        var url = "/api/v1/auth/oauth/consent?request=" + Uri.EscapeDataString(request);
        (await factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var browser = await _kit.BrowserAsync();
        var pat = factory.CreateClient();
        pat.DefaultRequestHeaders.Authorization = new("Bearer", (await McpTestClient.TokenAsync(browser, "printers:list")).Credential);
        (await pat.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await OAuthKit.DecideAsync(pat, request, true, "printers.read")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var app = await _kit.ConnectAsync(browser);
        var asApp = factory.CreateClient();
        asApp.DefaultRequestHeaders.Authorization = new("Bearer", app.Access);
        (await asApp.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.DecideAsync(asApp, request, true, "printers.read")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // A cross-origin page cannot post a decision even with the bearer.
        using var cross = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/oauth/consent")
        {
            Content = JsonContent.Create(new ConsentBody(request, true, ["printers.read"])),
        };
        cross.Headers.Add("Origin", "https://evil.example");
        (await browser.SendAsync(cross)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deny_ReturnsAccessDenied_WithStateAndIssuer_AndUsesUpTheRequest()
    {
        var client = await _kit.ClientAsync(redirect: "https://client.example/cb?tenant=7");
        var request = await _kit.RequestAsync(client, OAuthKit.Pkce().Challenge, state: "s-1");
        var browser = await _kit.BrowserAsync();
        using var response = await OAuthKit.DecideAsync(browser, request, false);
        var redirect = new Uri((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUri").GetString()!);
        redirect.GetLeftPart(UriPartial.Path).Should().Be("https://client.example/cb");
        OAuthKit.Param(redirect, "tenant").Should().Be("7");
        OAuthKit.Param(redirect, "error").Should().Be("access_denied");
        OAuthKit.Param(redirect, "state").Should().Be("s-1");
        OAuthKit.Param(redirect, "iss").Should().Be("https://farm.test");
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().BrowserSessions.AsNoTracking().Where(s => s.ClientId == client.Id).ToListAsync();
        rows.Should().ContainSingle().Which.RevokedAtUtc.Should().NotBeNull();

        using var approve = await OAuthKit.DecideAsync(browser, request, true, "printers.read");
        approve.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("invalid_request");
    }

    [Fact]
    public async Task Approve_ReturnsTheCode_KeepsTheRegisteredQuery_AndIsSingleUse()
    {
        var client = await _kit.ClientAsync(redirect: "https://client.example/cb?tenant=7");
        var request = await _kit.RequestAsync(client, OAuthKit.Pkce().Challenge, state: "s-2");
        var browser = await _kit.BrowserAsync();
        var redirect = await OAuthKit.ApproveAsync(browser, request, "printers.read", "print-jobs.cancel");
        redirect.GetLeftPart(UriPartial.Path).Should().Be("https://client.example/cb");
        OAuthKit.Param(redirect, "tenant").Should().Be("7");
        OAuthKit.Param(redirect, "code").Should().NotBeEmpty();
        OAuthKit.Param(redirect, "state").Should().Be("s-2");
        OAuthKit.Param(redirect, "iss").Should().Be("https://farm.test");

        using var again = await OAuthKit.DecideAsync(browser, request, true, "printers.read");
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("invalid_request");
        (await browser.GetAsync("/api/v1/auth/oauth/consent?request=" + Uri.EscapeDataString(request))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var pending = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().BrowserSessions.AsNoTracking().Where(s => s.ClientId == client.Id).ToListAsync();
        pending.Should().ContainSingle().Which.Scopes.Should().Equal("printers.read", "print-jobs.cancel");
    }

    [Fact]
    public async Task ExpiredTamperedOrUnknownRequestsAreRefused()
    {
        var client = await _kit.ClientAsync();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OpenFarm.Infrastructure.Auth.ServerConfiguration>().InitializeAsync(CancellationToken.None);
        var past = ActivatorUtilities.CreateInstance<OAuthService>(scope.ServiceProvider, new PastTime(TimeSpan.FromMinutes(11)));
        var expired = await past.BeginAsync(OAuthKit.AuthorizeQuery(client, OAuthKit.Pkce().Challenge).ToDictionary(p => p.Key, p => p.Value!), CancellationToken.None);
        var valid = await _kit.RequestAsync(client, OAuthKit.Pkce().Challenge);
        var browser = await _kit.BrowserAsync();
        foreach (var request in new[] { expired, valid[..^2] + "AA", "nonsense", "" })
        {
            using var response = await browser.GetAsync("/api/v1/auth/oauth/consent?request=" + Uri.EscapeDataString(request));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("title").GetString().Should().Be("Connection request expired");
            problem.GetProperty("code").GetString().Should().Be("invalid_request");
        }

        // A client removed after the request was made.
        await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().OAuthClients.Where(c => c.Id == client.Id).ExecuteDeleteAsync();
        (await browser.GetAsync("/api/v1/auth/oauth/consent?request=" + Uri.EscapeDataString(valid))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ScopesMustBeKnownAndHeldNow()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var viewer = new ApplicationUser { UserName = "viewer-" + Guid.NewGuid().ToString("N")[..8], Email = $"{Guid.NewGuid():N}@example.test" };
        (await users.CreateAsync(viewer, "viewer-password")).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(viewer, "Viewers");
        var browser = await _kit.BrowserAsync(viewer.UserName!, "viewer-password");
        var client = await _kit.ClientAsync();
        var request = await _kit.RequestAsync(client, OAuthKit.Pkce().Challenge);

        var described = await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/oauth/consent?request=" + Uri.EscapeDataString(request));
        var cancel = described.GetProperty("tiers")[2].GetProperty("scopes").EnumerateArray().Single(s => s.GetProperty("scope").GetString() == "print-jobs.cancel");
        cancel.GetProperty("allowed").GetBoolean().Should().BeFalse();

        foreach (var scopes in new[] { Array.Empty<string>(), ["print-jobs.cancel"], ["printers.read", "nope"], ["printers.read", "print-jobs.cancel"] })
        {
            using var response = await OAuthKit.DecideAsync(browser, request, true, scopes);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("code").GetString().Should().Be("invalid_scope");
            problem.GetProperty("detail").GetString().Should().Be("Select at least one available permission.");
        }
        // A refused selection does not use up the request.
        await OAuthKit.ApproveAsync(browser, request, "printers.read");
    }
}
