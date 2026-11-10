using FastEndpoints;
using OpenFarm.Api.Hosting;
using OpenFarm.Api.Security;

namespace OpenFarm.Api.Endpoints.Auth;

/// <param name="McpUrl">The MCP server URL from ORIGIN; null when ORIGIN is unset (the web uses its own address).</param>
/// <param name="SignInEnabled">Whether AI assistants can sign in with OAuth. Off until the server enables it.</param>
public sealed record ConnectedAppsResponse(string? McpUrl, bool SignInEnabled);

[Permission("auth:self")]
public sealed class ListConnectedAppsEndpoint(PublicOrigin origin) : EndpointWithoutRequest<ConnectedAppsResponse>
{
    public override void Configure() { Get("/api/v1/auth/apps"); Policies("Permission"); }

    public override Task HandleAsync(CancellationToken ct)
    {
        Response = new(origin.Value is { } value ? value + "/mcp" : null, false);
        return Task.CompletedTask;
    }
}
