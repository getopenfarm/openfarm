using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class ScimProvisioningTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Scim_RequiresItsOwnCredential_AndCannotAuthenticateApplicationRequests()
    {
        await using var kit = new ScimKit(factory);
        using var anonymous = kit.Client(false);
        var response = await anonymous.GetAsync("/scim/v2/Users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be(ScimProtocol.MediaType);
        (await ReadAsync(response))["schemas"]![0]!.GetValue<string>().Should().Be(ScimProtocol.ErrorSchema);
        using var client = kit.Client();
        (await client.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        login.EnsureSuccessStatusCode();
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        (await anonymous.GetAsync("/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Discovery_AdvertisesImplementedCapabilitiesAndSchemas()
    {
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        var config = await ReadAsync(await client.GetAsync("/scim/v2/ServiceProviderConfig"));
        config["patch"]!["supported"]!.GetValue<bool>().Should().BeTrue();
        config["bulk"]!["supported"]!.GetValue<bool>().Should().BeFalse();
        config["changePassword"]!["supported"]!.GetValue<bool>().Should().BeFalse();
        foreach (var path in new[] { "/ResourceTypes", "/Schemas", "/ResourceTypes/User", "/Schemas/" + ScimProtocol.UserSchema })
            (await client.GetAsync("/scim/v2" + path)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/scim/v2/Bulk", new StringContent("{}", Encoding.UTF8, ScimProtocol.MediaType))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserLifecycle_SupportsLookupPaginationPutPatchAndOptimisticConcurrency()
    {
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        var input = User();
        var created = await SendAsync(client, HttpMethod.Post, "/Users", input);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.CacheControl!.NoStore.Should().BeTrue();
        created.Headers.Location!.OriginalString.Should().StartWith("https://farm.example/scim/v2/Users/");
        var resource = await ReadAsync(created);
        var id = resource["id"]!.GetValue<string>();
        var etag = created.Headers.ETag!.ToString();
        var filtered = await ReadAsync(await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString($"externalId eq \"{input["externalId"]!.GetValue<string>()}\"")));
        filtered["totalResults"]!.GetValue<int>().Should().Be(1);
        filtered["Resources"]![0]!["id"]!.GetValue<string>().Should().Be(id);
        var zero = await ReadAsync(await client.GetAsync("/scim/v2/Users?count=0"));
        zero["itemsPerPage"]!.GetValue<int>().Should().Be(0);
        zero["totalResults"]!.GetValue<int>().Should().BeGreaterThan(0);
        var projection = await ReadAsync(await client.GetAsync($"/scim/v2/Users/{id}?attributes=userName"));
        projection.ContainsKey("id").Should().BeTrue();
        projection.ContainsKey("emails").Should().BeFalse();

        input["displayName"] = "Updated colleague";
        var updated = await SendAsync(client, HttpMethod.Put, "/Users/" + id, input, etag);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(updated))["displayName"]!.GetValue<string>().Should().Be("Updated colleague");
        (await SendAsync(client, HttpMethod.Put, "/Users/" + id, input, etag)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        var patch = Patch(new JsonObject { ["op"] = "replace", ["path"] = "name.givenName", ["value"] = "Changed" });
        (await SendAsync(client, HttpMethod.Patch, "/Users/" + id, patch)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(await client.GetAsync("/scim/v2/Users/" + id)))["name"]!["givenName"]!.GetValue<string>().Should().Be("Changed");
        var addedName = await SendAsync(client, HttpMethod.Patch, "/Users/" + id, Patch(new JsonObject
        { ["op"] = "add", ["path"] = "name", ["value"] = new JsonObject { ["familyName"] = "Colleague" } }));
        addedName.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(addedName))["name"]!["givenName"]!.GetValue<string>().Should().Be("Changed");
        var secondary = await SendAsync(client, HttpMethod.Patch, "/Users/" + id, Patch(new JsonObject
        { ["op"] = "add", ["path"] = "emails", ["value"] = new JsonArray(new JsonObject { ["value"] = "secondary@example.test" }) }));
        secondary.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(await client.GetAsync("/scim/v2/Users/" + id)))["emails"]![0]!["value"]!.GetValue<string>().Should().Be(input["emails"]![0]!["value"]!.GetValue<string>());
        input["externalId"] = "another-subject";
        var immutable = await SendAsync(client, HttpMethod.Put, "/Users/" + id, input);
        immutable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(immutable))["scimType"]!.GetValue<string>().Should().Be("mutability");
    }

    [Fact]
    public async Task Groups_GrantOnlyMappedRoles_AndPreserveIndependentLocalMemberships()
    {
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        var user = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/Users", User()));
        var userId = Guid.Parse(user["id"]!.GetValue<string>());
        var name = "farm-operators";
        var group = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/Groups", Group(name, userId)));
        var path = "/Groups/" + group["id"]!.GetValue<string>();
        var userAfterMembership = await client.GetAsync("/scim/v2/Users/" + userId);
        userAfterMembership.Headers.ETag!.ToString().Should().NotBe(user["meta"]!["version"]!.GetValue<string>());
        var rename = await SendAsync(client, HttpMethod.Patch, path, Patch(new JsonObject
        {
            ["op"] = "replace",
            ["value"] = new JsonObject
            { ["schemas"] = new JsonArray(ScimProtocol.GroupSchema), ["externalId"] = group["externalId"]!.DeepClone(), ["displayName"] = name },
        }));
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = kit.App.Services.CreateScope())
        {
            var access = await scope.ServiceProvider.GetRequiredService<PermissionService>().ResolveAsync(userId, CancellationToken.None);
            access.Groups.Should().Contain("Operators").And.Contain("Viewers");
            access.IsAdministrator.Should().BeFalse();
        }
        var removed = await SendAsync(client, HttpMethod.Patch, path, Patch(new JsonObject
        { ["op"] = "remove", ["path"] = $"members[value eq \"{userId.ToString().ToUpperInvariant()}\"]" }));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = kit.App.Services.CreateScope())
        {
            var access = await scope.ServiceProvider.GetRequiredService<PermissionService>().ResolveAsync(userId, CancellationToken.None);
            access.Groups.Should().NotContain("Operators").And.Contain("Viewers");
        }
        var unmapped = await SendAsync(client, HttpMethod.Post, "/Groups", Group("Administrators", userId));
        unmapped.StatusCode.Should().Be(HttpStatusCode.Created);
        using (var scope = kit.App.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PermissionService>().ResolveAsync(userId, CancellationToken.None)).IsAdministrator.Should().BeFalse();
        using (var scope = kit.App.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.AddToRoleAsync((await users.FindByIdAsync(userId.ToString()))!, "Operators")).Succeeded.Should().BeTrue();
        }
        (await SendAsync(client, HttpMethod.Delete, path)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/scim/v2" + path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var scope = kit.App.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PermissionService>().ResolveAsync(userId, CancellationToken.None)).Groups.Should().Contain("Operators");
    }

    [Fact]
    public async Task Deactivation_RevokesAllCredentialTypes_AndJitCannotReactivateDeletedAccounts()
    {
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        var input = User();
        var resource = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/Users", input));
        var id = Guid.Parse(resource["id"]!.GetValue<string>());
        string browserToken;
        string pat;
        using (var scope = kit.App.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<BrowserSessionService>();
            browserToken = (await sessions.CreateAsync(id, "SCIM test", CancellationToken.None, external: true)).Token;
            pat = (await scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>().CreateAsync(id,
                new("SCIM token", ["printers:list"], null), CancellationToken.None)).Credential;
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var app = new OAuthClient { Id = Guid.NewGuid().ToString(), Name = "Test app", RedirectUris = ["https://app.example/cb"], AuthMethod = "none", CreatedAtUtc = DateTimeOffset.UtcNow };
            db.OAuthClients.Add(app);
            db.BrowserSessions.Add(new BrowserSession
            {
                Id = Guid.NewGuid(),
                UserId = id,
                ClientId = app.Id,
                Scopes = [],
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastSeenAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }
        using var bearer = kit.Client(false);
        bearer.DefaultRequestHeaders.Authorization = new("Bearer", browserToken);
        (await bearer.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, HttpMethod.Patch, "/Users/" + id, Patch(new JsonObject { ["op"] = "replace", ["path"] = "active", ["value"] = false }))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bearer.GetAsync("/api/v1/auth/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        bearer.DefaultRequestHeaders.Authorization = new("Bearer", pat);
        (await bearer.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var scope = kit.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.BrowserSessions.AnyAsync(s => s.UserId == id && s.RevokedAtUtc == null)).Should().BeFalse();
            (await scope.ServiceProvider.GetRequiredService<OidcAccountService>().SignInAsync(input["externalId"]!.GetValue<string>(), "test", CancellationToken.None,
                new(input["userName"]!.GetValue<string>(), input["emails"]![0]!["value"]!.GetValue<string>(), "User", true))).Should().BeNull();
        }
        (await SendAsync(client, HttpMethod.Patch, "/Users/" + id, Patch(new JsonObject { ["op"] = "replace", ["path"] = "active", ["value"] = true }))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bearer.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(client, HttpMethod.Delete, "/Users/" + id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/scim/v2/Users/" + id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var scope = kit.App.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<OidcAccountService>().SignInAsync(input["externalId"]!.GetValue<string>(), "test", CancellationToken.None,
                new(input["userName"]!.GetValue<string>(), input["emails"]![0]!["value"]!.GetValue<string>(), "User", true))).Should().BeNull();
        var restored = await SendAsync(client, HttpMethod.Post, "/Users", input);
        restored.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync(restored))["id"]!.GetValue<string>().Should().Be(id.ToString());
        (await bearer.GetAsync("/api/v1/printers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LocalAccountsCannotBeTakenOver_AndManagedAccountsCannotGainPasswords()
    {
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        using var scope = kit.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "admin");
        (await client.GetAsync("/scim/v2/Users/" + admin.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var collision = User(); collision["userName"] = "admin";
        (await SendAsync(client, HttpMethod.Post, "/Users", collision)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var managedInput = User(); managedInput["displayName"] = " Test colleague ";
        var created = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/Users", managedInput));
        var id = Guid.Parse(created["id"]!.GetValue<string>());
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.ResetPasswordAsync(id, "temporary-password", admin.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.RemoveUserAsync(id, admin.Id, CancellationToken.None));
        var managed = (await accounts.ListUsersAsync(CancellationToken.None)).Single(u => u.Id == id);
        await accounts.SaveUserAsync(id, new AccountInput(managed.Username, managed.Email, managed.Name,
            managed.Groups.Select(g => g.Id).ToArray(), managed.ExtraGrants, false, null), admin.Id, CancellationToken.None);
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).IsEnabled.Should().BeFalse();
        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<ExternalProvisioningService>().FindAllowedRoleAsync("Administrators", CancellationToken.None));
        (await SendAsync(client, HttpMethod.Post, "/Groups", Group("local-member", admin.Id))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task JitAndScim_ConvergeOnTheSameAccountWithoutEmailMatching()
    {
        await using var kit = new ScimKit(factory);
        var suffix = Guid.NewGuid().ToString("N");
        Guid id;
        using (var scope = kit.App.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<OidcAccountService>().SignInAsync(suffix, "first JIT", CancellationToken.None,
                new("jit-" + suffix, suffix + "@example.test", "JIT user", true)))!.UserId;
        using var client = kit.Client();
        var result = await ReadAsync(await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString($"externalId eq \"{suffix}\"")));
        result["Resources"]![0]!["id"]!.GetValue<string>().Should().Be(id.ToString());
        var input = User(); input["externalId"] = suffix;
        (await SendAsync(client, HttpMethod.Put, "/Users/" + id, input)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var final = kit.App.Services.CreateScope();
        (await final.ServiceProvider.GetRequiredService<OidcAccountService>().SignInAsync(suffix, "next OIDC", CancellationToken.None))!.UserId.Should().Be(id);
    }

    [Theory]
    [InlineData("userName co \"example\"", "invalidFilter")]
    [InlineData("userName eq \"x\" or id pr", "invalidFilter")]
    public async Task UnsupportedFiltersReturnScimErrors(string filter, string error)
    {
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        var response = await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString(filter));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response))["scimType"]!.GetValue<string>().Should().Be(error);
    }

    [Fact]
    public async Task PatchFailuresAreAtomic_AndDuplicateRequestsCannotCreateDuplicateIdentities()
    {
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        var body = User();
        var responses = await Task.WhenAll(SendAsync(client, HttpMethod.Post, "/Users", body), SendAsync(client, HttpMethod.Post, "/Users", body));
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var resource = await ReadAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Created));
        var path = "/Users/" + resource["id"]!.GetValue<string>();
        var failed = await SendAsync(client, HttpMethod.Patch, path, Patch(
            new JsonObject { ["op"] = "replace", ["path"] = "active", ["value"] = false },
            new JsonObject { ["op"] = "add", ["path"] = "roles", ["value"] = new JsonArray("Administrators") }));
        failed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(await client.GetAsync("/scim/v2" + path)))["active"]!.GetValue<bool>().Should().BeTrue();
        var invalidSelection = await SendAsync(client, HttpMethod.Post, "/Users?attributes=name.givenName", User());
        invalidSelection.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DisabledScim_AndMalformedRequestsFailWithBoundedScimResponses()
    {
        using var disabled = factory.CreateClient();
        (await disabled.GetAsync("/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var kit = new ScimKit(factory);
        using var client = kit.Client();
        foreach (var json in new[] { "{", "[]", "{\"active\":true,\"Active\":false}", "{}" })
        {
            var response = await client.PostAsync("/scim/v2/Users", new StringContent(json, Encoding.UTF8, ScimProtocol.MediaType));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            response.Content.Headers.ContentType!.MediaType.Should().Be(ScimProtocol.MediaType);
        }
        var oversized = await client.PostAsync("/scim/v2/Users", new StringContent(new string('x', 262145), Encoding.UTF8, ScimProtocol.MediaType));
        oversized.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task ProvisioningCannotTargetProtectedDefaultGroups()
    {
        await using var kit = new ScimKit(factory, defaultGroup: "Administrators");
        ((Action)(() => kit.Client())).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RateLimitingReturnsScimErrors()
    {
        await using var kit = new ScimKit(factory, limit: 1);
        using var client = kit.Client();
        (await client.GetAsync("/scim/v2/ServiceProviderConfig")).StatusCode.Should().Be(HttpStatusCode.OK);
        var limited = await client.GetAsync("/scim/v2/ServiceProviderConfig");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType!.MediaType.Should().Be(ScimProtocol.MediaType);
        (await ReadAsync(limited))["status"]!.GetValue<string>().Should().Be("429");
    }

    private static JsonObject User()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new()
        {
            ["schemas"] = new JsonArray(ScimProtocol.UserSchema),
            ["externalId"] = suffix,
            ["userName"] = "scim-" + suffix,
            ["active"] = true,
            ["displayName"] = "Test colleague",
            ["emails"] = new JsonArray(new JsonObject { ["value"] = suffix + "@example.test", ["primary"] = true, ["type"] = "work" })
        };
    }
    private static JsonObject Group(string name, Guid userId) => new()
    {
        ["schemas"] = new JsonArray(ScimProtocol.GroupSchema),
        ["externalId"] = Guid.NewGuid().ToString(),
        ["displayName"] = name,
        ["members"] = new JsonArray(new JsonObject { ["value"] = userId.ToString() })
    };
    private static JsonObject Patch(params JsonObject[] operations) => new()
    { ["schemas"] = new JsonArray(ScimProtocol.PatchSchema), ["Operations"] = new JsonArray(operations.Select(o => (JsonNode)o).ToArray()) };
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, JsonObject? body = null, string? etag = null)
    {
        using var request = new HttpRequestMessage(method, "/scim/v2" + path);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, ScimProtocol.MediaType);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }
    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response)
    {
        return (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
    private sealed class ScimKit : IAsyncDisposable
    {
        public const string Token = "scim-test-provisioning-credential-0123456789";
        public WebApplicationFactory<Program> App { get; }
        public ScimKit(CustomWebApplicationFactory parent, string defaultGroup = "Viewers", int limit = 300) => App = parent.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ORIGIN", "https://farm.example");
            builder.ConfigureServices(services => services.AddScoped(_ => new AuthenticationConfiguration(new AuthenticationSettingsValues
            {
                OidcEnabled = true,
                Authority = "https://identity.example/application/o/farm/",
                ClientId = "farm",
                ClientSecret = "test-client-credential",
                JitEnabled = true,
                ScimEnabled = true,
                ScimToken = Token,
                DefaultGroup = defaultGroup,
                ScimRequestsPerMinute = limit,
                GroupMappings = new() { ["farm-operators"] = "Operators" },
            })));
        });
        public HttpClient Client(bool authenticated = true)
        {
            var client = App.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://farm.example"), AllowAutoRedirect = false });
            if (authenticated) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            return client;
        }
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
