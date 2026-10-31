using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public class SessionManagementTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<(HttpClient Client, string Token)> LoginAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return (client, login.Token);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task LoginAndRefresh_ShortAccessToken_FixedFourteenDaySession_RevocationIsImmediate()
    {
        var (client, token) = await LoginAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(10));
        var sid = Guid.Parse(jwt.Claims.Single(c => c.Type == "sid").Value);
        var before = await db.BrowserSessions.AsNoTracking().SingleAsync(s => s.Id == sid);
        before.ExpiresAtUtc.Should().BeCloseTo(before.CreatedAtUtc.AddDays(14), TimeSpan.FromSeconds(1));
        (await RefreshAsync(client)).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await db.BrowserSessions.AsNoTracking().SingleAsync(s => s.Id == sid);
        after.ExpiresAtUtc.Should().Be(before.ExpiresAtUtc);
        (await db.SessionCredentials.CountAsync(c => c.SessionId == sid)).Should().Be(2);
        (await client.DeleteAsync($"/api/v1/auth/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(client)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshWithoutAntiforgery_IsRejected()
    {
        var (client, _) = await LoginAsync();
        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UntrustedOrigin_CannotLogIn()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rotation_RetryIsIdempotent_ReplayOutsideGraceRevokesSession()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<BrowserSessionService>();
        var user = await db.Users.SingleAsync(u => u.UserName == "admin");
        var initial = await service.CreateAsync(user.Id, "Test", CancellationToken.None);
        var first = await service.RefreshAsync(initial.RefreshToken!, CancellationToken.None);
        var retry = await service.RefreshAsync(initial.RefreshToken!, CancellationToken.None);
        retry!.RefreshToken.Should().Be(first!.RefreshToken);
        var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(initial.Token).Claims.Single(c => c.Type == "sid").Value);
        var old = await db.SessionCredentials.SingleAsync(c => c.UsedAtUtc != null && c.ProtectedSuccessor != null && c.SessionId == sid);
        old.UsedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-11);
        await db.SaveChangesAsync();
        (await service.RefreshAsync(initial.RefreshToken!, CancellationToken.None)).Should().BeNull();
        (await service.RefreshAsync(first.RefreshToken!, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Permissions_AreEnforcedAndChangesApplyWithoutNewToken()
    {
        var (client, token) = await LoginAsync();
        var userId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject);
        // Test an independent ordinary account so the fixture administrator keeps full access.
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "viewer-test", Email = "viewer-test@example.test" };
        (await manager.CreateAsync(user, "test-password")).Succeeded.Should().BeTrue();
        await manager.AddToRoleAsync(user, "Viewers");
        var session = await scope.ServiceProvider.GetRequiredService<BrowserSessionService>().CreateAsync(user.Id, "Test", CancellationToken.None);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        (await client.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/api/v1/parts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/settings/general")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await manager.AddClaimAsync(user, new System.Security.Claims.Claim(PermissionCatalog.GrantClaim, "settings:general:read"));
        (await client.GetAsync("/api/v1/settings/general")).StatusCode.Should().Be(HttpStatusCode.OK);
        var administration = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        await administration.SaveUserAsync(user.Id, new(user.UserName!, user.Email!, null, [], [], false, null), userId, CancellationToken.None);
        (await client.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LastEnabledAdministrator_CannotBeDisabled()
    {
        var (client, _) = await LoginAsync();
        var users = (await client.GetFromJsonAsync<AccountResponse[]>("/api/v1/admin/users"))!;
        var admin = users.Single(u => u.Username == "admin");
        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{admin.Id}", new UpdateAccountRequest(admin.Id, admin.Username, admin.Email, admin.Name, [], [], false));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TemporaryPassword_GivesGroupAccess_ChangeKeepsCallerSession_ResetRevokesSessions()
    {
        var (client, _) = await LoginAsync();
        var groups = (await client.GetFromJsonAsync<GroupResponse[]>("/api/v1/admin/groups"))!;
        var create = await client.PostAsJsonAsync("/api/v1/admin/users", new AccountInput("temporary-test", "temporary@example.test", "Temporary", [groups.Single(g => g.Name == "Viewers").Id], [], true, "temporary-password"));
        create.EnsureSuccessStatusCode();
        var id = (await create.Content.ReadFromJsonAsync<EntityIdResponse>())!.Id;
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrowserSessionService>();
        var session = await service.CreateAsync(id, "Test", CancellationToken.None);
        var other = await service.CreateAsync(id, "Other", CancellationToken.None);
        var restricted = factory.CreateClient();
        restricted.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        (await restricted.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        // No forced change: the temporary password already grants the user's group access.
        (await restricted.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.OK);
        var wrong = await restricted.PostAsJsonAsync("/api/v1/auth/change-password", new PasswordInput("not-the-password", "replacement-password"));
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Current password is incorrect.");
        (await restricted.PostAsJsonAsync("/api/v1/auth/change-password", new PasswordInput("temporary-password", "replacement-password"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // The caller stays signed in; every other session is revoked.
        (await restricted.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new("Bearer", other.Token);
        (await otherClient.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"/api/v1/admin/users/{id}/reset-password", new ResetAccountPasswordRequest(id, "reset-password"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await restricted.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    [Fact]
    public async Task ConcurrentRefreshes_ReturnTheSameSuccessor()
    {
        LoginResult initial;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == "admin");
            initial = await scope.ServiceProvider.GetRequiredService<BrowserSessionService>().CreateAsync(user.Id, "Test", CancellationToken.None);
        }
        async Task<LoginResult?> Refresh()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<BrowserSessionService>().RefreshAsync(initial.RefreshToken!, CancellationToken.None);
        }
        var results = await Task.WhenAll(Refresh(), Refresh(), Refresh());
        results.Should().OnlyContain(r => r != null);
        results.Select(r => r!.RefreshToken).Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

}
