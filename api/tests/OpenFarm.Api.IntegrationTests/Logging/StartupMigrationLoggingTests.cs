using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using OpenFarm.Api.Logging;
using OpenFarm.Infrastructure.Persistence;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace OpenFarm.Api.IntegrationTests.Logging;

public class StartupMigrationLoggingTests
{
    [Fact]
    public async Task RealMigrations_FreshAndRepeatRunsHideProbeButPermissionFailureRemainsVisible()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18")
            .WithDatabase("migration_logging")
            .WithUsername("migration_admin")
            .WithPassword("migration-test-password")
            .Build();
        await postgres.StartAsync();

        var raw = new CollectingSink();
        var visible = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(raw)
            .WriteTo.Logger(filtered => filtered.Filter.With<ExpectedMigrationProbeFilter>().WriteTo.Sink(visible))
            .CreateLogger();
        using var loggerFactory = new SerilogLoggerFactory(logger, dispose: false);
        var options = new DbContextOptionsBuilder<OpenFarmDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseLoggerFactory(loggerFactory)
            .Options;
        await using var context = new OpenFarmDbContext(options);

        const string historyQuery = "SELECT \"MigrationId\", \"ProductVersion\"\nFROM \"__EFMigrationsHistory\"\nORDER BY \"MigrationId\";";
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(historyQuery));
        Assert.Contains(visible.Events, IsCommandError);
        raw.Events.Clear();
        visible.Events.Clear();

        await StartupMigrationRunner.RunAsync(context, logger);

        var probe = Assert.Single(raw.Events, IsCommandError);
        Assert.Null(probe.Exception);
        Assert.DoesNotContain(visible.Events, e => e.Level >= LogEventLevel.Error);
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());

        raw.Events.Clear();
        visible.Events.Clear();
        await StartupMigrationRunner.RunAsync(context, logger);
        Assert.DoesNotContain(raw.Events, e => e.Level >= LogEventLevel.Error);

        await context.Database.ExecuteSqlRawAsync("CREATE ROLE migration_reader LOGIN PASSWORD 'reader-test-password'; GRANT USAGE ON SCHEMA public TO migration_reader;");
        var restricted = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Username = "migration_reader",
            Password = "reader-test-password",
        };
        var restrictedOptions = new DbContextOptionsBuilder<OpenFarmDbContext>()
            .UseNpgsql(restricted.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .Options;
        await using var restrictedContext = new OpenFarmDbContext(restrictedOptions);
        visible.Events.Clear();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => StartupMigrationRunner.RunAsync(restrictedContext, logger));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        Assert.Contains(visible.Events, e => e.MessageTemplate.Text == "Database migration failed" && e.Exception == failure);
        logger.Error("After migration failure");
        Assert.False(visible.Events.Last().Properties.ContainsKey("StartupMigration"));
    }

    private static bool IsCommandError(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("EventId", out var value)
        && value is StructureValue details
        && details.Properties.Any(p => p.Name == "Id"
            && p.Value is ScalarValue { Value: int id } && id == RelationalEventId.CommandError.Id);

    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
