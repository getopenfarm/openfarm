using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Infrastructure.Auth.OAuth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.OAuth;

[Collection("OAuth")]
public sealed class OAuthTokenTests(OAuthFactory factory)
{
    private readonly OAuthKit _kit = new(factory);

    private sealed class PastTime(TimeSpan ago) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() - ago;
    }

    private async Task<(OAuthApp Client, Uri Redirect, string Verifier)> ApprovedAsync(string method = "none", params string[] scopes)
    {
        var client = await _kit.ClientAsync(method);
        var (verifier, challenge) = OAuthKit.Pkce();
        var redirect = await OAuthKit.ApproveAsync(await _kit.BrowserAsync(), await _kit.RequestAsync(client, challenge), scopes.Length > 0 ? scopes : ["printers.read"]);
        return (client, redirect, verifier);
    }

    private async Task<int> ActivityAsync(Guid appId, string eventType)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().ActivityLogEntries
            .CountAsync(a => a.EntityType == "ConnectedApp" && a.EntityId == appId.ToString() && a.EventType == eventType);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("client_secret_post")]
    [InlineData("client_secret_basic")]
    public async Task CodeExchange_IssuesAnMcpTokenAndARefreshCredential(string method)
    {
        var (client, redirect, verifier) = await ApprovedAsync(method, "printers.read", "print-jobs.read");
        using var response = await _kit.PostAsync(client, OAuthKit.CodeForm(client, redirect, verifier));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.Pragma.ToString().Should().Be("no-cache");
        var (access, refresh, scope) = await OAuthKit.TokensAsync(response);
        refresh.Should().MatchRegex("^ofr_[0-9a-f]{64}$");
        scope.Should().Be("printers.read print-jobs.read");
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(access);
        jwt.Issuer.Should().Be("https://farm.test");
        jwt.Audiences.Should().Equal("https://farm.test/mcp");
        jwt.Claims.Single(c => c.Type == "client_id").Value.Should().Be(client.Id);
        (await _kit.McpStatusAsync(access)).Should().Be(HttpStatusCode.OK);
        (await ActivityAsync(OAuthKit.SessionId(access), "ConnectedAppAdded")).Should().Be(1);
    }

    [Fact]
    public async Task ClientAuthenticationMustMatchTheRegisteredMethod()
    {
        var (client, redirect, verifier) = await ApprovedAsync("client_secret_basic");
        var form = OAuthKit.CodeForm(client, redirect, verifier);
        foreach (var wrong in new[]
        {
            client with { Method = "none" },
            client with { Method = "client_secret_post" },
            client with { Secret = "ofs_" + new string('0', 64) },
        })
        {
            using var response = await _kit.PostAsync(wrong, form);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await OAuthKit.ErrorAsync(response)).Should().Be("invalid_client");
        }
        using var badBasic = await _kit.PostAsync(client with { Secret = "wrong" }, form);
        badBasic.Headers.WwwAuthenticate.ToString().Should().Be("Basic realm=\"openfarm-oauth\"");

        // Basic and a body secret together, or a body client id that differs from Basic's.
        using var both = await _kit.PostAsync(client, new(form) { ["client_secret"] = client.Secret! });
        both.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var mismatch = await _kit.PostAsync(client, new(form) { ["client_id"] = "ofc_other" });
        mismatch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // None of the failures used the code.
        await OAuthKit.TokensAsync(await _kit.PostAsync(client, form));
    }

    [Fact]
    public async Task WrongVerifierBurnsTheCode()
    {
        var (client, redirect, verifier) = await ApprovedAsync();
        var wrong = new Dictionary<string, string>(OAuthKit.CodeForm(client, redirect, verifier)) { ["code_verifier"] = OAuthKit.Pkce().Verifier };
        using var first = await _kit.PostAsync(client, wrong);
        (await OAuthKit.ErrorAsync(first)).Should().Be("invalid_grant");
        using var second = await _kit.PostAsync(client, OAuthKit.CodeForm(client, redirect, verifier));
        (await OAuthKit.ErrorAsync(second)).Should().Be("invalid_grant");

        var missing = new Dictionary<string, string>(OAuthKit.CodeForm(client, redirect, verifier));
        missing.Remove("code_verifier");
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, missing))).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task AReplayedCodeRevokesTheApp()
    {
        var (client, redirect, verifier) = await ApprovedAsync();
        var form = OAuthKit.CodeForm(client, redirect, verifier);
        var (access, refresh, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(client, form));
        using var replay = await _kit.PostAsync(client, form);
        (await OAuthKit.ErrorAsync(replay)).Should().Be("invalid_grant");
        (await _kit.McpStatusAsync(access)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, OAuthKit.RefreshForm(refresh)))).Should().Be("invalid_grant");
        (await ActivityAsync(OAuthKit.SessionId(access), "ConnectedAppRevoked")).Should().Be(1);
    }

    [Fact]
    public async Task TheCodeIsBoundToItsClientRedirectAndResource()
    {
        var (client, redirect, verifier) = await ApprovedAsync();
        var form = OAuthKit.CodeForm(client, redirect, verifier);
        var other = await _kit.ClientAsync();
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(other, form))).Should().Be("invalid_grant");
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, new(form) { ["redirect_uri"] = "http://127.0.0.1:9999/cb" }))).Should().Be("invalid_grant");
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, new(form) { ["resource"] = "https://farm.test" }))).Should().Be("invalid_target");
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, new(form) { ["grant_type"] = "password" }))).Should().Be("unsupported_grant_type");
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, new(form) { ["code"] = "garbage" }))).Should().Be("invalid_grant");
        // Without resource is fine; the refused attempts did not touch the code.
        var withoutResource = new Dictionary<string, string>(form);
        withoutResource.Remove("resource");
        await OAuthKit.TokensAsync(await _kit.PostAsync(client, withoutResource));
    }

    [Fact]
    public async Task AnExpiredCodeIsRefused()
    {
        var client = await _kit.ClientAsync();
        var (verifier, challenge) = OAuthKit.Pkce();
        var request = await _kit.RequestAsync(client, challenge);
        using var scope = factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Users.SingleAsync(u => u.UserName == "admin");
        var past = ActivatorUtilities.CreateInstance<OAuthService>(scope.ServiceProvider, new PastTime(TimeSpan.FromMinutes(3)));
        var outcome = await past.DecideAsync(request, admin.Id, true, ["printers.read"], CancellationToken.None);
        var code = outcome.Query.Single(p => p.Key == "code").Value!;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = client.Redirect,
            ["code_verifier"] = verifier,
        };
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, form))).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ConcurrentExchanges_OnlyOneSucceeds()
    {
        var (client, redirect, verifier) = await ApprovedAsync();
        var form = OAuthKit.CodeForm(client, redirect, verifier);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _kit.PostAsync(client, form)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);
        // The loser is a replay with the right verifier: it revokes the app, the winner's tokens included (RFC 6749 §4.1.2).
        var (access, refresh, _) = await OAuthKit.TokensAsync(responses.Single(r => r.StatusCode == HttpStatusCode.OK));
        (await _kit.McpStatusAsync(access)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, OAuthKit.RefreshForm(refresh)))).Should().Be("invalid_grant");
        (await ActivityAsync(OAuthKit.SessionId(access), "ConnectedAppRevoked")).Should().Be(1);
    }

    [Fact]
    public async Task AReplayWithoutTheVerifierChangesNothing()
    {
        var (client, redirect, verifier) = await ApprovedAsync();
        var form = OAuthKit.CodeForm(client, redirect, verifier);
        var (access, _, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(client, form));
        // Someone who saw the code in a URL, but not the verifier, cannot disconnect the app.
        var wrong = new Dictionary<string, string>(form) { ["code_verifier"] = OAuthKit.Pkce().Verifier };
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, wrong))).Should().Be("invalid_grant");
        var missing = new Dictionary<string, string>(form);
        missing.Remove("code_verifier");
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, missing))).Should().Be("invalid_grant");
        (await _kit.McpStatusAsync(access)).Should().Be(HttpStatusCode.OK);
        (await ActivityAsync(OAuthKit.SessionId(access), "ConnectedAppRevoked")).Should().Be(0);
    }

    [Fact]
    public async Task AppsEndThirtyDaysAfterConsent_WhateverTheRefreshes()
    {
        var app = await _kit.ConnectAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var expires = await db.BrowserSessions.Where(s => s.Id == app.AppId).Select(s => s.ExpiresAtUtc).SingleAsync();
        var created = await db.BrowserSessions.Where(s => s.Id == app.AppId).Select(s => s.CreatedAtUtc).SingleAsync();
        expires.Should().Be(created.AddDays(30));
        var (access, next, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh)));
        (await db.BrowserSessions.Where(s => s.Id == app.AppId).Select(s => s.ExpiresAtUtc).SingleAsync()).Should().Be(expires);

        await db.BrowserSessions.Where(s => s.Id == app.AppId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        (await _kit.McpStatusAsync(access)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(next)))).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task AnExpiredAccessTokenGetsInvalidToken()
    {
        var app = await _kit.ConnectAsync();
        using var scope = factory.Services.CreateScope();
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Jwt:SigningKey"]!));
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var original = handler.ReadJwtToken(app.Access);
        var expired = handler.WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(OAuthFactory.Origin, OAuthFactory.Resource,
            original.Claims.Where(c => c.Type is "sub" or "sid" or "client_id"), notBefore: DateTime.UtcNow.AddMinutes(-30), expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", expired);
        using var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().EndWith("error=\"invalid_token\"");
    }

    [Theory]
    [InlineData("client_secret_basic")]
    [InlineData("client_secret_post")]
    public async Task ConfidentialClientsNeedTheirSecretToRefreshAndRevoke(string method)
    {
        var app = await _kit.ConnectAsync(method: method);
        var wrong = app.Client with { Secret = "ofs_" + new string('1', 64) };
        using (var refused = await _kit.PostAsync(wrong, OAuthKit.RefreshForm(app.Refresh)))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await OAuthKit.ErrorAsync(refused)).Should().Be("invalid_client");
        }
        (await _kit.PostAsync(wrong, new() { ["token"] = app.Refresh }, "/oauth/revoke")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var (access, next, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh)));
        (await _kit.McpStatusAsync(access)).Should().Be(HttpStatusCode.OK);
        (await _kit.PostAsync(app.Client, new() { ["token"] = next }, "/oauth/revoke")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _kit.McpStatusAsync(access)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACodeIsRefusedOnceItsUserIsDisabled()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<OpenFarm.Infrastructure.Identity.ApplicationUser>>();
        var user = new OpenFarm.Infrastructure.Identity.ApplicationUser { UserName = "disabled-" + Guid.NewGuid().ToString("N")[..8], Email = $"{Guid.NewGuid():N}@example.test" };
        (await users.CreateAsync(user, "disabled-password")).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, "Viewers");
        var client = await _kit.ClientAsync();
        var (verifier, challenge) = OAuthKit.Pkce();
        var redirect = await OAuthKit.ApproveAsync(await _kit.BrowserAsync(user.UserName!, "disabled-password"), await _kit.RequestAsync(client, challenge), "printers.read");
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsEnabled, false));
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, OAuthKit.CodeForm(client, redirect, verifier)))).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Refresh_Rotates_RetriesWithinTenSecondsGetTheSamePair_AndLaterReuseRevokes()
    {
        var app = await _kit.ConnectAsync(scopes: ["printers.read", "print-jobs.read"]);
        var (access, next, scope) = await OAuthKit.TokensAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh)));
        next.Should().NotBe(app.Refresh).And.MatchRegex("^ofr_[0-9a-f]{64}$");
        scope.Should().Be("printers.read print-jobs.read");
        OAuthKit.SessionId(access).Should().Be(app.AppId);

        var (_, retried, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh)));
        retried.Should().Be(next);

        // Narrowing is accepted (the full grant comes back); widening is refused without rotating.
        var narrowed = new Dictionary<string, string>(OAuthKit.RefreshForm(next)) { ["scope"] = "printers.read openid" };
        var (_, afterNarrow, narrowScope) = await OAuthKit.TokensAsync(await _kit.PostAsync(app.Client, narrowed));
        narrowScope.Should().Be("printers.read print-jobs.read");
        var widened = new Dictionary<string, string>(OAuthKit.RefreshForm(afterNarrow)) { ["scope"] = "print-jobs.cancel" };
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, widened))).Should().Be("invalid_scope");
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, new(OAuthKit.RefreshForm(afterNarrow)) { ["resource"] = "https://farm.test/other" })))
            .Should().Be("invalid_target");

        // Another client cannot use or revoke it.
        var other = await _kit.ClientAsync();
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(other, OAuthKit.RefreshForm(afterNarrow)))).Should().Be("invalid_grant");
        (await _kit.PostAsync(other, new() { ["token"] = afterNarrow }, "/oauth/revoke")).StatusCode.Should().Be(HttpStatusCode.OK);
        var (latest, last, _) = await OAuthKit.TokensAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(afterNarrow)));

        using (var scopeDb = factory.Services.CreateScope())
        {
            var db = scopeDb.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var hash = OpenFarm.Infrastructure.Auth.BrowserSessionService.Hash(app.Refresh);
            await db.SessionCredentials.Where(c => c.Hash == hash).ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAtUtc, DateTimeOffset.UtcNow.AddSeconds(-11)));
        }
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh)))).Should().Be("invalid_grant");
        (await _kit.McpStatusAsync(latest)).Should().Be(HttpStatusCode.Unauthorized);
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(app.Client, OAuthKit.RefreshForm(last)))).Should().Be("invalid_grant");
        (await ActivityAsync(app.AppId, "ConnectedAppCredentialReuse")).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentRefreshes_GetTheSameSuccessor()
    {
        var app = await _kit.ConnectAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _kit.PostAsync(app.Client, OAuthKit.RefreshForm(app.Refresh))));
        var refreshed = await Task.WhenAll(responses.Select(OAuthKit.TokensAsync));
        refreshed.Select(r => r.Refresh).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task ABrowserRefreshCredentialIsNotAnAppGrant()
    {
        var client = await _kit.ClientAsync();
        var browserCredential = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, OAuthKit.RefreshForm(browserCredential)))).Should().Be("invalid_grant");
        using var scope = factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<OpenFarm.Infrastructure.Auth.BrowserSessionService>();
        var admin = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Users.SingleAsync(u => u.UserName == "admin");
        var login = await sessions.CreateAsync(admin.Id, "test", CancellationToken.None);
        (await OAuthKit.ErrorAsync(await _kit.PostAsync(client, OAuthKit.RefreshForm(login.RefreshToken!)))).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task TheTokenEndpointRefusesNonFormsAndDuplicateParameters()
    {
        var client = await _kit.ClientAsync();
        using var json = await _kit.Raw().PostAsync("/oauth/token", new StringContent("{}", Encoding.UTF8, "application/json"));
        (await OAuthKit.ErrorAsync(json)).Should().Be("invalid_request");
        using var duplicate = await _kit.Raw().PostAsync("/oauth/token", new StringContent(
            $"grant_type=refresh_token&client_id={client.Id}&refresh_token=a&refresh_token=b", Encoding.UTF8, "application/x-www-form-urlencoded"));
        (await OAuthKit.ErrorAsync(duplicate)).Should().Be("invalid_request");
    }
}
