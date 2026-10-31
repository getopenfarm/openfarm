using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed class IdentityAuthenticationService(
    UserManager<ApplicationUser> userManager,
    BrowserSessionService sessions,
    PermissionService permissions,
    OpenFarmDbContext db) : IAuthenticationService
{
    public async Task<LoginResult?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await userManager.FindByNameAsync(username);
        if (user is null) return null;
        // Serialize password verification/session creation with password resets and account edits.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE", cancellationToken);
        await db.Entry(user).ReloadAsync(cancellationToken);
        if (!user.IsEnabled || await userManager.IsLockedOutAsync(user)) return null;
        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        await userManager.ResetAccessFailedCountAsync(user);
        var result = await sessions.CreateAsync(user.Id, string.Empty, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<WhoAmIResult?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsEnabled) return null;
        var access = await permissions.ResolveAsync(user.Id, cancellationToken);
        return new WhoAmIResult(user.Id, user.UserName ?? user.Email ?? string.Empty, user.Email ?? string.Empty)
        { DisplayName = user.DisplayName, IsAdministrator = access.IsAdministrator, Groups = access.Groups, Permissions = access.Permissions };
    }
}
