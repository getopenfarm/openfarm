using System.IdentityModel.Tokens.Jwt;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

public static class CredentialAccess
{
    /// <summary>
    /// The permissions this request may use: the owner's current permissions, narrowed to the
    /// token's abilities when the request came through a personal access token.
    /// </summary>
    public static async Task<IReadOnlySet<string>> EffectiveAsync(HttpContext http)
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
            return new HashSet<string>();
        var access = await http.RequestServices.GetRequiredService<PermissionService>().ResolveAsync(userId, http.RequestAborted);
        var effective = access.Permissions.ToHashSet(StringComparer.Ordinal);
        if (user.HasClaim(c => c.Type == "pat"))
            effective.IntersectWith(user.FindAll(PersonalAccessTokenHandler.PermissionClaim).Select(c => c.Value));
        return effective;
    }
}
