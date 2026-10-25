using Microsoft.AspNetCore.Identity;
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

    private async Task EnsureAdminUserAsync(CancellationToken cancellationToken)
    {
        var username = configuration["Seed:AdminUsername"];
        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // Without a first account nobody can log in, so the first boot needs the seed credentials.
            if (!await dbContext.Users.AnyAsync(cancellationToken))
            {
                throw new InvalidOperationException("Seed:AdminPassword is not configured; it is required to create the first administrator account.");
            }

            LogAdminSeedSkipped(logger);
            return;
        }

        var existingUser = await userManager.FindByNameAsync(username);
        if (existingUser is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed the administrator account: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed:AdminUsername/Seed:AdminEmail/Seed:AdminPassword are not configured; skipping operator seeding.")]
    private static partial void LogAdminSeedSkipped(ILogger logger);
}
