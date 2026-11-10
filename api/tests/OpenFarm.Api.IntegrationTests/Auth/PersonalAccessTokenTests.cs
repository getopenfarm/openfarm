using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Auth;

public sealed class PersonalAccessTokenTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> BrowserAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        return client;
    }

    private HttpClient Bearer(string credential)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", credential);
        return client;
    }

    private static async Task<AccessTokenCreated> CreateAsync(HttpClient browser, string[]? permissions = null)
    {
        var response = await browser.PostAsJsonAsync("/api/v1/auth/tokens", new AccessTokenInput(Guid.NewGuid().ToString(), permissions ?? ["printers:list"], DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6))));
        response.EnsureSuccessStatusCode();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<AccessTokenCreated>())!;
    }

    [Fact]
    public async Task ScopedCredential_IsHashOnly_AndCannotManageAccountsOrSessions()
    {
        var browser = await BrowserAsync();
        var created = await CreateAsync(browser);
        var token = Bearer(created.Credential);
        (await token.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await token.GetAsync("/api/v1/settings/general")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await token.DeleteAsync($"/api/v1/parts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var path in new[] { "/api/v1/auth/tokens", "/api/v1/auth/tokens/options", "/api/v1/auth/sessions", "/api/v1/admin/users" })
            (await token.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await token.PostAsJsonAsync("/api/v1/auth/tokens", new AccessTokenInput("escalation", ["printers:list"], null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await token.PostAsJsonAsync($"/api/v1/auth/tokens/{created.Token.Id}/regenerate", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await token.PostAsync("/hubs/farm/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var identity = (await token.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami"))!;
        identity.Permissions.Should().Equal("printers:list");
        identity.IsAdministrator.Should().BeFalse();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var stored = await db.PersonalAccessTokens.SingleAsync(t => t.Id == created.Token.Id);
        stored.CredentialHash.Should().Be(PersonalAccessTokenService.Hash(created.Credential));
        stored.LastUsedAtUtc.Should().NotBeNull();
        var list = await (await browser.GetAsync("/api/v1/auth/tokens")).Content.ReadAsStringAsync();
        list.Should().NotContain(created.Credential).And.NotContain(stored.CredentialHash);
        var audits = await db.ActivityLogEntries.Where(a => a.EntityId == created.Token.Id.ToString()).ToArrayAsync();
        audits.Should().ContainSingle(a => a.EventType == "AccessTokenCreated");
        System.Text.Json.JsonSerializer.Serialize(audits).Should().NotContain(created.Credential).And.NotContain(stored.CredentialHash);
    }

    [Fact]
    public async Task RegenerationInvalidatesOldCredential_RevocationInvalidatesReplacement()
    {
        var browser = await BrowserAsync();
        var created = await CreateAsync(browser);
        var response = await browser.PostAsJsonAsync($"/api/v1/auth/tokens/{created.Token.Id}/regenerate", new { });
        response.EnsureSuccessStatusCode();
        var next = (await response.Content.ReadFromJsonAsync<AccessTokenCreated>())!;
        next.Token.ExpiresAtUtc.Should().Be(created.Token.ExpiresAtUtc);
        next.Token.Permissions.Should().Equal(created.Token.Permissions);
        (await Bearer(created.Credential).GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Bearer(next.Credential).GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.DeleteAsync($"/api/v1/auth/tokens/{created.Token.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Bearer(next.Credential).GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LibraryAndCompositePermissionsRequireEveryScopedAbility()
    {
        var browser = await BrowserAsync();
        var libraryOnly = await CreateAsync(browser, ["library:view"]);
        (await Bearer(libraryOnly.Credential).GetAsync("/api/v1/library/parts/folders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var libraryWithParts = await CreateAsync(browser, ["library:view", "parts:list"]);
        (await Bearer(libraryWithParts.Credential).GetAsync("/api/v1/library/parts/folders")).StatusCode.Should().Be(HttpStatusCode.OK);
        var jobsOnly = await CreateAsync(browser, ["print-jobs:view"]);
        (await Bearer(jobsOnly.Credential).GetAsync("/api/v1/notifications")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExpiredMalformedAndUnknownTokensFail_ExpiredTokensCannotRegenerate()
    {
        var browser = await BrowserAsync();
        var created = await CreateAsync(browser);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await db.PersonalAccessTokens.Where(t => t.Id == created.Token.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        foreach (var credential in new[] { created.Credential, "ofp_bad", "ofp_" + new string('a', 64) })
            (await Bearer(credential).GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await browser.PostAsJsonAsync($"/api/v1/auth/tokens/{created.Token.Id}/regenerate", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PermissionsAreBoundedByCurrentOwnerAccess_AndOwnershipIsEnforced()
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = Guid.NewGuid().ToString(), Email = "token@example.test" };
        (await manager.CreateAsync(user, "temporary-password")).Succeeded.Should().BeTrue();
        var grant = new Claim(PermissionCatalog.GrantClaim, "printers:list");
        await manager.AddClaimAsync(user, grant);
        var service = scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>();
        var created = await service.CreateAsync(user.Id, new("owner-test", ["printers:list"], null), CancellationToken.None);
        var token = Bearer(created.Credential);
        (await token.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.OK);
        await manager.RemoveClaimAsync(user, grant);
        (await token.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var browser = await BrowserAsync();
        // Self endpoints cannot target a different owner's credential, even for an admin.
        (await browser.DeleteAsync($"/api/v1/auth/tokens/{created.Token.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await browser.PostAsJsonAsync($"/api/v1/auth/tokens/{created.Token.Id}/regenerate", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await browser.GetAsync($"/api/v1/admin/users/{user.Id}/tokens")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.DeleteAsync($"/api/v1/admin/users/{user.Id}/tokens/{created.Token.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await token.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidInputsAreRejected_AndConcurrentCreationRespectsLimit()
    {
        var browser = await BrowserAsync();
        foreach (var input in new[] {
            new AccessTokenInput("", ["printers:list"], null),
            new AccessTokenInput("none", [], null),
            new AccessTokenInput("unknown", ["made-up"], null),
            new AccessTokenInput("administration", ["users:manage"], null),
            new AccessTokenInput("past", ["printers:list"], DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))) })
            (await browser.PostAsJsonAsync("/api/v1/auth/tokens", input)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = new ApplicationUser { UserName = Guid.NewGuid().ToString(), Email = "cap@example.test" };
        (await manager.CreateAsync(owner, "temporary-password")).Succeeded.Should().BeTrue();
        await manager.AddToRoleAsync(owner, "Viewers");
        for (var i = 0; i < 49; i++) db.PersonalAccessTokens.Add(new() { Id = Guid.NewGuid(), UserId = owner.Id, Name = $"existing-{i}", CredentialHash = Guid.NewGuid().ToString(), LastFour = "1234", Permissions = ["printers:list"], CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        async Task<bool> AttemptAsync()
        {
            using var inner = factory.Services.CreateScope();
            try { await inner.ServiceProvider.GetRequiredService<PersonalAccessTokenService>().CreateAsync(owner.Id, new(Guid.NewGuid().ToString(), ["printers:list"], null), CancellationToken.None); return true; }
            catch (ArgumentException) { return false; }
        }
        (await Task.WhenAll(AttemptAsync(), AttemptAsync())).Count(x => x).Should().Be(1);
        (await db.PersonalAccessTokens.CountAsync(t => t.UserId == owner.Id)).Should().Be(50);
    }

    [Fact]
    public async Task TokenWritesRecordTheTokenInTheActivityLog_BrowserWritesDoNot()
    {
        var browser = await BrowserAsync();
        var created = await CreateAsync(browser, ["vendors:create"]);
        var byToken = await Bearer(created.Credential).PostAsJsonAsync("/api/v1/vendors", new { Name = $"token-{Guid.NewGuid()}" });
        byToken.EnsureSuccessStatusCode();
        var byBrowser = await browser.PostAsJsonAsync("/api/v1/vendors", new { Name = $"browser-{Guid.NewGuid()}" });
        byBrowser.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        async Task<OpenFarm.Domain.ActivityLog.ActivityLogEntry> RowAsync(HttpResponseMessage response)
        {
            var id = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString();
            return await db.ActivityLogEntries.AsNoTracking().SingleAsync(a => a.EntityId == id && a.EventType == "VendorCreated");
        }
        (await RowAsync(byToken)).Details!.Via.Should().Be(new OpenFarm.Domain.ActivityLog.ActivityChannel("Token", created.Token.Id, created.Token.Name));
        (await RowAsync(byBrowser)).Details?.Via.Should().BeNull();
        var log = await browser.GetStringAsync("/api/v1/activity-log?entityType=Vendor&pageSize=100");
        log.Should().Contain($"\"via\":{{\"kind\":\"Token\",\"credentialId\":\"{created.Token.Id}\",\"label\":\"{created.Token.Name}\"}}");
    }

    [Fact]
    public async Task PasswordChangeResetAndDisableRevokeTokens()
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = new ApplicationUser { UserName = Guid.NewGuid().ToString(), Email = "changes@example.test" };
        (await manager.CreateAsync(owner, "temporary-password")).Succeeded.Should().BeTrue();
        await manager.AddToRoleAsync(owner, "Viewers");
        var tokens = scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        async Task<string> NewTokenAsync() => (await tokens.CreateAsync(owner.Id, new(Guid.NewGuid().ToString(), ["printers:list"], null), CancellationToken.None)).Credential;
        var credential = await NewTokenAsync();
        await accounts.ChangePasswordAsync(owner.Id, new("temporary-password", "updated-password"), null, CancellationToken.None);
        (await Bearer(credential).GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        credential = await NewTokenAsync();
        await accounts.ResetPasswordAsync(owner.Id, "reset-password", owner.Id, CancellationToken.None);
        (await Bearer(credential).GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        credential = await NewTokenAsync();
        await accounts.SaveUserAsync(owner.Id, new(owner.UserName!, owner.Email!, null, [], [], false, null), owner.Id, CancellationToken.None);
        (await Bearer(credential).GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await db.PersonalAccessTokens.AsNoTracking().Where(t => t.UserId == owner.Id).ToArrayAsync()).Should().OnlyContain(t => t.RevokedAtUtc != null);
    }
}
