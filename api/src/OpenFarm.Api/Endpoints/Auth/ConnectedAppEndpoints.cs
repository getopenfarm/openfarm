using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Hosting;
using OpenFarm.Api.Security;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Auth.OAuth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Endpoints.Auth;

/// <param name="McpUrl">The MCP server URL from the effective public URL; null when unset (the web uses its own address).</param>
/// <param name="SignInEnabled">Whether the effective MCP mode permits OAuth sign-in.</param>
/// <param name="McpEnabled">Whether the instance accepts MCP connections.</param>
/// <param name="Apps">The signed-in user's connected apps, newest first. Revoke one with DELETE /api/v1/auth/sessions/{id}.</param>
public sealed record ConnectedAppsResponse(string? McpUrl, bool SignInEnabled, ConnectedAppResponse[] Apps, bool McpEnabled = false);

[Permission("auth:self")]
public sealed class ListConnectedAppsEndpoint(PublicOrigin origin, OAuthSettings settings, OAuthService oauth, ServerConfiguration server) : EndpointWithoutRequest<ConnectedAppsResponse>
{
    public override void Configure() { Get("/api/v1/auth/apps"); Policies("Permission"); }

    public override async Task HandleAsync(CancellationToken ct) =>
        Response = new(origin.Value is { } value ? value + "/mcp" : null, settings.Enabled, await oauth.ListAppsAsync(SessionCookies.UserId(HttpContext), ct), server.Current.McpEnabled);
}

/// <summary>Another user's connected apps. Revoke one with DELETE /api/v1/admin/users/{id}/sessions/{sessionId}.</summary>
[Permission("users:manage")]
public sealed class ListUserConnectedAppsEndpoint(OpenFarmDbContext db, OAuthService oauth) : Endpoint<RevokeSessionRequest, ConnectedAppResponse[]>
{
    public override void Configure() { Get("/api/v1/admin/users/{id}/apps"); Policies("Permission"); }

    public override async Task HandleAsync(RevokeSessionRequest req, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == req.Id, ct)) { await Send.NotFoundAsync(ct); return; }
        Response = await oauth.ListAppsAsync(req.Id, ct);
    }
}
