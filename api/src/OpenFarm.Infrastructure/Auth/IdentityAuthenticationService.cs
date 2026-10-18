using Microsoft.AspNetCore.Identity;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Auth;

public sealed class IdentityAuthenticationService(
    UserManager<ApplicationUser> userManager,
    JwtTokenService jwtTokenService) : IAuthenticationService
{
    public async Task<LoginResult?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        // UserManager.CheckPasswordAsync alone never consults or updates lockout state — only
        // IsLockedOutAsync/AccessFailedAsync/ResetAccessFailedCountAsync do, so brute-forcing the
        // password would otherwise go unthrottled even though AccessFailedCount/LockoutEnd exist
        // in the schema and Options.Lockout is configured.
        if (await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, password);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var issuedToken = jwtTokenService.IssueToken(user.Id);
        return new LoginResult(issuedToken.Token, issuedToken.ExpiresAtUtc, user.Id);
    }

    public async Task<WhoAmIResult?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        return new WhoAmIResult(user.Id, user.UserName ?? user.Email ?? string.Empty, user.Email ?? string.Empty);
    }
}
