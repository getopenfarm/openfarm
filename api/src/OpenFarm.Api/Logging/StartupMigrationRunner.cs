using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace OpenFarm.Api.Logging;

public static class StartupMigrationRunner
{
    public static async Task RunAsync(DbContext context, Serilog.ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        using (LogContext.PushProperty("StartupMigration", true))
        {
            try
            {
                await context.Database.MigrateAsync();
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Database migration failed");
                throw;
            }
        }
    }
}
