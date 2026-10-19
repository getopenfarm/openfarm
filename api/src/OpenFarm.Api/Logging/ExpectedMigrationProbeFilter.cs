using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace OpenFarm.Api.Logging;

/// <summary>Hides only Npgsql's expected missing-history-table probe during migration startup.</summary>
public sealed class ExpectedMigrationProbeFilter : ILogEventFilter
{
    public bool IsEnabled(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // EF omits the exception from CommandError logs. StartupMigrationRunner reports and
        // rethrows real migration failures, so only this scoped history probe can be hidden.
        return !(logEvent.Properties.TryGetValue("StartupMigration", out var migration)
            && migration is ScalarValue { Value: true }
            && logEvent.Properties.TryGetValue("EventId", out var eventId)
            && eventId is StructureValue eventDetails
            && eventDetails.Properties.Any(p => p.Name == "Id"
                && p.Value is ScalarValue { Value: int id } && id == RelationalEventId.CommandError.Id)
            && logEvent.Properties.TryGetValue("SourceContext", out var source)
            && source is ScalarValue { Value: "Microsoft.EntityFrameworkCore.Database.Command" }
            && logEvent.Properties.TryGetValue("commandText", out var command)
            && command is ScalarValue { Value: string sql }
            && sql.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() ==
                "SELECT \"MigrationId\", \"ProductVersion\"\nFROM \"__EFMigrationsHistory\"\nORDER BY \"MigrationId\";");
    }
}
