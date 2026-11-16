using System.Net;
using System.Globalization;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.ActivityLog;

public class AuditLogTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task AuditWriteFailure_RollsBackBusinessChange()
    {
        var name = $"AUDIT-FAIL-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        // A real PostgreSQL audit constraint failure must roll back the accompanying vendor insert.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION reject_test_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."EntityLabel" LIKE 'AUDIT-FAIL-%' THEN RAISE EXCEPTION 'audit unavailable'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_test_audit BEFORE INSERT ON "ActivityLogEntries" FOR EACH ROW EXECUTE FUNCTION reject_test_audit();
            """);
        try
        {
            db.Vendors.Add(Vendor.Create(name));
            var save = () => db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
            db.ChangeTracker.Clear();
            (await db.Vendors.AnyAsync(v => v.Name == name)).Should().BeFalse();
            (await db.ActivityLogEntries.AnyAsync(e => e.EntityLabel == name)).Should().BeFalse();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_audit ON \"ActivityLogEntries\"; DROP FUNCTION reject_test_audit();");
        }
    }

    [Fact]
    public async Task FailedSessionAudit_RollsBackTheBulkRevocation()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var owner = await db.Users.Select(u => u.Id).FirstAsync();
        var session = new BrowserSession
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            LastSeenAtUtc = DateTimeOffset.UtcNow
        };
        db.BrowserSessions.Add(session);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION reject_session_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."EntityType" = 'BrowserSession' THEN RAISE EXCEPTION 'audit unavailable'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_session_audit BEFORE INSERT ON "ActivityLogEntries" FOR EACH ROW EXECUTE FUNCTION reject_session_audit();
            """);
        try
        {
            var sessions = scope.ServiceProvider.GetRequiredService<BrowserSessionService>();
            var revoke = () => sessions.RevokeAsync(owner, session.Id, owner, CancellationToken.None);
            await revoke.Should().ThrowAsync<DbUpdateException>();
            db.ChangeTracker.Clear();
            (await db.BrowserSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id)).RevokedAtUtc.Should().BeNull();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_session_audit ON \"ActivityLogEntries\"; DROP FUNCTION reject_session_audit();");
        }
    }

    [Fact]
    public async Task FiltersDetailsAndPrinterHistory_KeepObservationsOutOfAudit()
    {
        var client = await AuthenticatedClientAsync(factory.CreateClient());
        Guid printerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var printer = Printer.Create("Audit printer", "Bambu", "P1S", "10.0.0.1", $"AUDIT-{Guid.NewGuid():N}");
            printerId = printer.Id;
            db.Printers.Add(printer);
            await db.SaveChangesAsync();
            db.ActivityLogEntries.Add(ActivityLogEntry.Create("Printer", printerId.ToString(), "PrinterStatusObserved", null, ActorType.System,
                new ActivityLogDetails("RUNNING", "PAUSE")));
            await db.SaveChangesAsync();
        }
        var page = await client.GetFromJsonAsync<AuditLogPageResponse>($"/api/v1/audit-log?entityType=Printer&entityId={printerId}&action=create&channel=System");
        var entry = page!.Items.Should().ContainSingle().Subject;
        entry.EntityLabel.Should().Be("Audit printer");
        entry.Changes.Should().Contain(c => c.Field == "Name");
        entry.CorrelationId.Should().NotBeNullOrEmpty();
        var detail = await client.GetFromJsonAsync<AuditEntryResponse>($"/api/v1/audit-log/{entry.Id}");
        detail!.Href.Should().Be($"/printers/{printerId}");
        var activity = await client.GetFromJsonAsync<PrinterActivityPageResponse>($"/api/v1/printers/{printerId}/activity");
        activity!.Items.Should().Contain(e => e.EventType == "PrinterStatusObserved" && e.Details!.ReportedState == "PAUSE");
        var options = await client.GetAsync("/api/v1/audit-log/filter-options");
        options.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/activity-log")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.CreateClient().GetAsync("/api/v1/audit-log")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DateRange_IncludesSubMillisecondEndOfDay_AndExcludesNextMidnight()
    {
        var client = await AuthenticatedClientAsync(factory.CreateClient());
        var entityId = Guid.NewGuid().ToString();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            foreach (var instant in new[] { "2026-11-14T23:59:59Z", "2026-11-15T23:59:59.9995000Z", "2026-11-16T00:00:00Z" })
            {
                var entry = ActivityLogEntry.Create("DateRangeTest", entityId, "Observed", null, ActorType.System);
                db.ActivityLogEntries.Add(entry);
                db.Entry(entry).Property(e => e.TimestampUtc).CurrentValue = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);
            }
            await db.SaveChangesAsync();
        }
        var result = await client.GetFromJsonAsync<AuditLogPageResponse>($"/api/v1/audit-log?entityType=DateRangeTest&entityId={entityId}&from=2026-11-15T00:00:00Z&to=2026-11-16T00:00:00Z");
        result!.Items.Should().ContainSingle();
        result.Items[0].TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-11-15T23:59:59.9995000Z", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task PrinterHistory_UsesOrderedAndAssociationIndexes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var printerId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "ActivityLogEntries" ("Id", "EntityType", "EntityId", "EventType", "ActorType", "TimestampUtc", "Category", "RelatedPrinterIds")
            SELECT gen_random_uuid(), 'Printer', CASE WHEN i % 100 = 0 THEN CAST(@id AS text) ELSE gen_random_uuid()::text END,
              'PrinterStatusObserved', 'System', now() - i * interval '1 second', 'MachineObservation',
              CASE WHEN i % 100 = 0 THEN ARRAY[CAST(@id AS uuid)] ELSE ARRAY[gen_random_uuid()] END
            FROM generate_series(1, 5000) AS i;
            ANALYZE "ActivityLogEntries";
            """, new NpgsqlParameter("id", printerId));
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Parameters.Add(new NpgsqlParameter("id", printerId));
        command.CommandText = """
            EXPLAIN (FORMAT JSON) SELECT * FROM "ActivityLogEntries"
            WHERE "EntityType" = 'Printer' AND "EntityId" = CAST(@id AS text)
            ORDER BY "TimestampUtc" DESC, "Id" LIMIT 25;
            """;
        (await command.ExecuteScalarAsync())!.ToString().Should().Contain("IX_ActivityLogEntries_EntityType_EntityId_TimestampUtc_Id");
        command.CommandText = """
            EXPLAIN (FORMAT JSON) SELECT * FROM "ActivityLogEntries"
            WHERE "RelatedPrinterIds" @> ARRAY[CAST(@id AS uuid)];
            """;
        (await command.ExecuteScalarAsync())!.ToString().Should().Contain("IX_ActivityLogEntries_RelatedPrinterIds");
    }

    [Fact]
    public async Task ExplicitTransactionRollback_LeavesNoAuditOrBusinessRecord()
    {
        var name = $"Rollback-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Vendors.Add(Vendor.Create(name));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        (await db.Vendors.AnyAsync(v => v.Name == name)).Should().BeFalse();
        (await db.ActivityLogEntries.AnyAsync(e => e.EntityLabel == name)).Should().BeFalse();
    }
}
