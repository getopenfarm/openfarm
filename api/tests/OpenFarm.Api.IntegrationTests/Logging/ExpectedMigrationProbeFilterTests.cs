using Serilog.Context;
using OpenFarm.Api.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace OpenFarm.Api.IntegrationTests.Logging;

public class ExpectedMigrationProbeFilterTests
{
    private const string HistoryQuery = "SELECT \"MigrationId\", \"ProductVersion\"\nFROM \"__EFMigrationsHistory\"\nORDER BY \"MigrationId\";";

    [Theory]
    [InlineData(true, HistoryQuery, "Microsoft.EntityFrameworkCore.Database.Command", 20102, false)]
    [InlineData(false, HistoryQuery, "Microsoft.EntityFrameworkCore.Database.Command", 20102, true)]
    [InlineData(true, "SELECT * FROM printers;", "Microsoft.EntityFrameworkCore.Database.Command", 20102, true)]
    [InlineData(true, HistoryQuery, "OpenFarm.Startup", 20102, true)]
    [InlineData(true, HistoryQuery, "Microsoft.EntityFrameworkCore.Database.Command", 20101, true)]
    public void Filter_RequiresMigrationContextAndExactCommandEvent(bool migration, string query, string source, int eventId, bool expectedVisible)
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Filter.With<ExpectedMigrationProbeFilter>()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var scope = LogContext.PushProperty("StartupMigration", migration);

        // EF's command-error event has no Exception attached.
        logger.ForContext("SourceContext", source)
            .ForContext("EventId", new { Id = eventId }, destructureObjects: true)
            .Error("Failed executing DbCommand {commandText}", query);

        Assert.Equal(expectedVisible, sink.Events.Count == 1);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
