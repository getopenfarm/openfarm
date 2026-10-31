using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Hubs;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class LiveEventAuthorizationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Delivery_RechecksPermissionAndSession_AndDeniesUnknownEvents()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "live-viewer", Email = "live-viewer@example.test" };
        (await users.CreateAsync(user, "test-password")).Succeeded.Should().BeTrue();
        var grant = new System.Security.Claims.Claim(PermissionCatalog.GrantClaim, "printers:get-detail");
        await users.AddClaimAsync(user, grant);
        var sessionService = scope.ServiceProvider.GetRequiredService<BrowserSessionService>();
        var login = await sessionService.CreateAsync(user.Id, "Test", CancellationToken.None);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        var sid = Guid.Parse(jwt.Claims.Single(c => c.Type == "sid").Value);
        var connections = new FarmConnections();
        connections.All["active"] = new(user.Id, sid, login.ExpiresAtUtc);
        var proxy = new Capture();
        var sender = new AuthorizedFarmClients(new FakeHub(proxy), factory.Services.GetRequiredService<IServiceScopeFactory>(), connections);
        await sender.SendCoreAsync("CameraFrameReceived", []);
        proxy.Sends.Should().Be(1);
        await users.RemoveClaimAsync(user, grant);
        await sender.SendCoreAsync("CameraFrameReceived", []);
        proxy.Sends.Should().Be(1);
        await users.AddClaimAsync(user, grant);
        await sessionService.RevokeAsync(user.Id, sid, user.Id, CancellationToken.None);
        await sender.SendCoreAsync("CameraFrameReceived", []);
        await sender.SendCoreAsync("UnknownEvent", []);
        proxy.Sends.Should().Be(1);
    }

    private sealed class Capture : IClientProxy
    {
        public int Sends { get; private set; }
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) { Sends++; return Task.CompletedTask; }
    }
    private sealed class FakeHub(IClientProxy proxy) : IHubContext<FarmHub>
    {
        public IHubClients Clients { get; } = new ClientsProxy(proxy);
        public IGroupManager Groups => throw new NotSupportedException();
    }
    private sealed class ClientsProxy(IClientProxy proxy) : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}
