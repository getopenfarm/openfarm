using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.Auth;

[Permission("auth:whoami")]
public sealed class WhoAmIEndpoint(IQueryHandler<WhoAmIQuery, WhoAmIResult?> handler)
    : EndpointWithoutRequest<WhoAmIResult>
{
    public override void Configure()
    {
        Get("/api/v1/auth/whoami");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(sub, out var userId))
        {
            await HttpContext.Response.SendUnauthorizedAsync(ct);
            return;
        }

        var result = await handler.HandleAsync(new WhoAmIQuery(userId), ct);
        if (result is null)
        {
            await HttpContext.Response.SendUnauthorizedAsync(ct);
            return;
        }

        Response = result;
    }
}
