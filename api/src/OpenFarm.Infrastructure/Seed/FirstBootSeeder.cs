using Microsoft.AspNetCore.Identity;
using OpenFarm.Application.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenFarm.Domain.Settings;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Seed;

public sealed partial class FirstBootSeeder(
    OpenFarmDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration,
    ILogger<FirstBootSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFarmSettingsAsync(cancellationToken);
        await EnsureAdminUserAsync(cancellationToken);
    }

    private async Task EnsureFarmSettingsAsync(CancellationToken cancellationToken)
    {
        var exists = await dbContext.FarmSettings.FindAsync([FarmSettings.SingletonId], cancellationToken);
        if (exists is not null)
        {
            return;
        }

        dbContext.FarmSettings.Add(FarmSettings.Create());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private const string DefaultAdminPassword = "admin-openfarm";

    private async Task EnsureAdminUserAsync(CancellationToken cancellationToken)
    {
        var username = configuration["Seed:AdminUsername"];
        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
        {
            LogAdminSeedSkipped(logger);
            return;
        }

        var existingUser = await userManager.FindByNameAsync(username);
        if (existingUser is not null)
        {
            return;
        }

        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
        {
            // An install that already has accounts never gets a surprise administrator.
            if (await dbContext.Users.AnyAsync(cancellationToken))
            {
                return;
            }

            password = DefaultAdminPassword;
        }

        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, password!);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed the administrator account: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        var membership = await userManager.AddToRoleAsync(user, PermissionCatalog.Administrators);
        if (!membership.Succeeded) throw new InvalidOperationException("Failed to assign the seeded administrator group.");

        if (generated)
        {
            LogDefaultAdminPassword(logger, username);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed:AdminUsername/Seed:AdminEmail are not configured; skipping operator seeding.")]
    private static partial void LogAdminSeedSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Created administrator '{Username}' with the default password. Change it, or set Seed:AdminPassword before the first start.")]
    private static partial void LogDefaultAdminPassword(ILogger logger, string username);
}
