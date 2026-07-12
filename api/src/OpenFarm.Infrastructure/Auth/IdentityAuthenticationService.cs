using Microsoft.AspNetCore.Identity;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.Organizations;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Auth;

public sealed class IdentityAuthenticationService(
    UserManager<ApplicationUser> userManager,
    JwtTokenService jwtTokenService) : IAuthenticationService
{
    public async Task<LoginResult?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(username);
        if (user is null)
        {
            return null;
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, password);
        if (!passwordValid)
        {
            return null;
        }

        var issuedToken = jwtTokenService.IssueToken(user.Id, Organization.WellKnownId);
        return new LoginResult(issuedToken.Token, issuedToken.ExpiresAtUtc, user.Id, Organization.WellKnownId);
    }

    public async Task<WhoAmIResult?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        return new WhoAmIResult(user.Id, user.UserName ?? user.Email ?? string.Empty, Organization.WellKnownId);
    }
}
