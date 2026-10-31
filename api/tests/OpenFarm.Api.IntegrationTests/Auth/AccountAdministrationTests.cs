using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class AccountAdministrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static int _logins;

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        // Login is throttled per client address; give each login its own.
        var login = Interlocked.Increment(ref _logins);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new LoginCommand("admin", "admin-openfarm")),
        };
        request.Headers.Add("X-Forwarded-For", $"10.1.{login / 256 % 256}.{login % 256}");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        return client;
    }

    private static async Task<Guid> CreateUserAsync(HttpClient admin, string username)
    {
        var groups = (await admin.GetFromJsonAsync<GroupResponse[]>("/api/v1/admin/groups"))!;
        var create = await admin.PostAsJsonAsync("/api/v1/admin/users",
            new AccountInput(username, $"{username}@example.test", null, [groups.Single(g => g.Name == "Viewers").Id], [], true, "temporary"));
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<EntityIdResponse>())!.Id;
    }

    private async Task<(HttpClient Client, Guid SessionId)> SignInAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<BrowserSessionService>().CreateAsync(userId, "Test", CancellationToken.None);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        var sid = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims.Single(c => c.Type == "sid").Value;
        return (client, Guid.Parse(sid));
    }

    [Fact]
    public async Task PermissionCatalog_ReturnsAreasAndPermissions()
    {
        var admin = await AdminAsync();
        var catalog = (await admin.GetFromJsonAsync<PermissionCatalogResponse>("/api/v1/admin/permissions"))!;
        catalog.Areas.Select(a => a.Key).Should().Equal(PermissionCatalog.Areas.Select(a => a.Key));
        catalog.Permissions.Select(p => p.Key).Should().Equal(PermissionCatalog.All.Select(p => p.Key));
        catalog.Permissions.Single(p => p.Key == "activity-log:list").IsExplicit.Should().BeTrue();
    }

    [Fact]
    public async Task ListGroups_DropsUnknownGrants_AndSortsByCatalog()
    {
        var admin = await AdminAsync();
        var create = await admin.PostAsJsonAsync("/api/v1/admin/groups", new GroupInput("Sorted", "", ["tags:list", "printers:list"]));
        var id = (await create.Content.ReadFromJsonAsync<EntityIdResponse>())!.Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            db.RoleClaims.Add(new IdentityRoleClaim<Guid> { RoleId = id, ClaimType = PermissionCatalog.GrantClaim, ClaimValue = "retired:permission" });
            await db.SaveChangesAsync();
        }
        var group = (await admin.GetFromJsonAsync<GroupResponse[]>("/api/v1/admin/groups"))!.Single(g => g.Id == id);
        group.Grants.Should().Equal("printers:list", "tags:list");
    }

    [Fact]
    public async Task RemoveUser_DeletesTheAccount_RefusesSelf()
    {
        var admin = await AdminAsync();
        var id = await CreateUserAsync(admin, "remove-test");
        var (removed, _) = await SignInAsync(id);
        (await admin.DeleteAsync($"/api/v1/admin/users/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<AccountResponse[]>("/api/v1/admin/users"))!.Should().NotContain(u => u.Id == id);
        (await removed.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.DeleteAsync($"/api/v1/admin/users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var self = (await admin.GetFromJsonAsync<AccountResponse[]>("/api/v1/admin/users"))!.Single(u => u.Username == "admin");
        var refused = await admin.DeleteAsync($"/api/v1/admin/users/{self.Id}");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Cannot delete your own account");
    }

    [Fact]
    public async Task AdminSessions_ListRevokeOneAndAll()
    {
        var admin = await AdminAsync();
        var id = await CreateUserAsync(admin, "sessions-test");
        var (first, firstId) = await SignInAsync(id);
        var (second, _) = await SignInAsync(id);
        (await admin.GetFromJsonAsync<SessionResponse[]>($"/api/v1/admin/users/{id}/sessions"))!.Should().HaveCount(2);

        (await admin.DeleteAsync($"/api/v1/admin/users/{id}/sessions/{firstId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await first.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetFromJsonAsync<SessionResponse[]>($"/api/v1/admin/users/{id}/sessions"))!.Should().ContainSingle();

        (await admin.DeleteAsync($"/api/v1/admin/users/{id}/sessions")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await second.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetFromJsonAsync<SessionResponse[]>($"/api/v1/admin/users/{id}/sessions"))!.Should().BeEmpty();
        (await admin.GetAsync($"/api/v1/admin/users/{Guid.NewGuid()}/sessions")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A signed-in user without users:manage is refused, not just signed out.
        var (viewer, _) = await SignInAsync(await CreateUserAsync(admin, "sessions-viewer"));
        (await viewer.GetAsync($"/api/v1/admin/users/{id}/sessions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/v1/admin/users/{id}/sessions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/v1/admin/users/{id}/sessions/{firstId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/v1/admin/users/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateProfile_ChangesOwnDetails_ValidatesInput()
    {
        var admin = await AdminAsync();
        var id = await CreateUserAsync(admin, "profile-test");
        var (client, _) = await SignInAsync(id);

        (await client.PutAsJsonAsync("/api/v1/auth/profile", new ProfileInput("  Mei Ling Tan ", " mei@example.test ", " mei "))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var me = (await client.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami"))!;
        me.DisplayName.Should().Be("Mei Ling Tan");
        me.Email.Should().Be("mei@example.test");
        me.Username.Should().Be("mei");

        (await client.PutAsJsonAsync("/api/v1/auth/profile", new ProfileInput("", "mei@example.test", "mei"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<WhoAmIResult>("/api/v1/auth/whoami"))!.DisplayName.Should().BeNull();

        foreach (var (input, message) in new[]
        {
            (new ProfileInput(null, " ", "mei"), "Email is required."),
            (new ProfileInput(null, "mei@example.test", ""), "Username is required."),
            (new ProfileInput(null, "not-an-email", "mei"), "Enter a valid email address."),
            (new ProfileInput(null, "mei@example.test", "admin"), "Email or username is already taken."),
            (new ProfileInput(null, "admin@openfarm.local", "mei"), "Email or username is already taken."),
        })
        {
            var response = await client.PutAsJsonAsync("/api/v1/auth/profile", input);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain(message);
        }
    }

    [Fact]
    public async Task Passwords_NeedEightCharactersAndNothingElse()
    {
        var admin = await AdminAsync();
        var groups = (await admin.GetFromJsonAsync<GroupResponse[]>("/api/v1/admin/groups"))!;
        var viewers = groups.Single(g => g.Name == "Viewers").Id;
        (await admin.PostAsJsonAsync("/api/v1/admin/users", new AccountInput("short-test", "short@example.test", null, [viewers], [], true, "1234567")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/admin/users", new AccountInput("plain-test", "plain@example.test", null, [viewers], [], true, "12345678")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
