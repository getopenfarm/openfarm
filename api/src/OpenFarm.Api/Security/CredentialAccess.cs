using System.IdentityModel.Tokens.Jwt;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

public static class CredentialAccess
{
    /// <summary>
    /// The permissions this request may use: the owner's current permissions, narrowed to the token's abilities
    /// for a personal access token, or to the expansion of the granted scopes for a connected app. Administrators
    /// are narrowed the same way. Each permission belongs to one scope, so this also gates tools on scope.
    /// A browser session gets nothing unless <paramref name="browser"/>: then, on REST, it holds the account's permissions.
    /// </summary>
    public static async Task<IReadOnlySet<string>> EffectiveAsync(HttpContext http, bool browser = false)
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
            return new HashSet<string>();
        var access = await http.RequestServices.GetRequiredService<PermissionService>().ResolveAsync(userId, http.RequestAborted);
        var effective = access.Permissions.ToHashSet(StringComparer.Ordinal);
        if (user.HasClaim(c => c.Type == CredentialClaims.Pat))
            effective.IntersectWith(user.FindAll(PersonalAccessTokenHandler.PermissionClaim).Select(c => c.Value));
        else if (CredentialClaims.IsApp(user))
            effective.IntersectWith(OAuthScopes.Expand(user.FindAll(CredentialClaims.Scope).Select(c => c.Value)));
        else if (!browser)
            effective.Clear(); // Only tokens and apps reach MCP; anything else gets nothing.
        return effective;
    }
}
