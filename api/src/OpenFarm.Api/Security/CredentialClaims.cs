using System.Security.Claims;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

/// <summary>Claims that say which non-browser credential a request came through.</summary>
public static class CredentialClaims
{
    /// <summary>A personal access token's id.</summary>
    public const string Pat = "pat";

    /// <summary>A connected app's OAuth client id; its <c>sid</c> is the app's session id.</summary>
    public const string ClientId = JwtTokenService.ClientIdClaim;

    /// <summary>One granted OAuth scope per claim, added after the app session is checked.</summary>
    public const string Scope = "openfarm.oauth-scope";

    public const string AppName = "openfarm.app-name";

    public static bool IsApp(ClaimsPrincipal user) => user.HasClaim(c => c.Type == ClientId);

    /// <summary>The token or app this request acts through, or null for a browser session or background work.</summary>
    public static ActivityChannel? Channel(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        if (Guid.TryParse(user.FindFirst(Pat)?.Value, out var tokenId))
            return new ActivityChannel(ActivityChannel.Token, tokenId, user.FindFirst(PersonalAccessTokenHandler.NameClaim)?.Value ?? "");
        if (IsApp(user) && Guid.TryParse(user.FindFirst("sid")?.Value, out var appId))
            return new ActivityChannel(ActivityChannel.App, appId, user.FindFirst(AppName)?.Value ?? "");
        return null;
    }
}
