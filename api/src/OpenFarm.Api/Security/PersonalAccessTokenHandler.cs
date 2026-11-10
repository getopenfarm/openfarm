using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

public sealed class PersonalAccessTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, PersonalAccessTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PersonalAccessToken";
    public const string PermissionClaim = "openfarm.token-permission";
    public const string NameClaim = "openfarm.token-name";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/api/v1") && !Request.Path.StartsWithSegments("/mcp"))
            return AuthenticateResult.Fail("Personal access tokens are only supported by the REST API and /mcp.");
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var row = await tokens.AuthenticateAsync(header[7..], Context.RequestAborted);
        if (row is null) return AuthenticateResult.Fail("Invalid access token.");
        var claims = new List<Claim> { new("sub", row.UserId.ToString()), new("pat", row.Id.ToString()), new(NameClaim, row.Name) };
        claims.AddRange(row.Permissions.Select(p => new Claim(PermissionClaim, p)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!Request.Path.StartsWithSegments("/mcp")) return base.HandleChallengeAsync(properties);
        // MCP clients read the challenge to learn how to authenticate (RFC 6750 §3).
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = Request.Headers.Authorization.Count > 0
            ? "Bearer realm=\"openfarm-mcp\", error=\"invalid_token\""
            : "Bearer realm=\"openfarm-mcp\"";
        return Task.CompletedTask;
    }
}
