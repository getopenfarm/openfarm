using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class SnapshotMigrationTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task Upgrade_PreservesLegacyReferencesAndNames_ThenRecoversVerifiedSnapshot()
    {
        _ = await ClientAsync();
        using var scope = Factory.CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var snapshotMigration = migrations.FindIndex(m => m.EndsWith("_IndependentProductionSnapshots", StringComparison.Ordinal));
        var previous = migrations[snapshotMigration - 1];
        var latest = migrations[^1];
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        var part = await PartAsync();
        var runId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var requirementId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var tenant = db.TenantInfo!.Id;
        var organization = Organization.WellKnownId;
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProductionRuns" ("Id", "OrganizationId", "Name", "Status", "CreatedAtUtc", "UpdatedAtUtc", "TenantId")
            VALUES ({runId}, {organization}, 'Legacy migration', 'Active', {now}, {now}, {tenant});
            INSERT INTO "RunLines" ("Id", "OrganizationId", "ProductionRunId", "TargetKind", "TargetId", "Quantity", "RevisionMode",
                "DefaultAssignmentMode", "BackfillPrinterCap", "TenantId")
            VALUES ({lineId}, {organization}, {runId}, 'Part', {part.Id}, 1, 'FollowLatest', 'Unassigned', 1, {tenant});
            INSERT INTO "PartRequirements" ("Id", "OrganizationId", "RunLineId", "PartId", "Quantity", "TenantId")
            VALUES ({requirementId}, {organization}, {lineId}, {part.Id}, 1, {tenant});
            INSERT INTO "PrintJobs" ("Id", "OrganizationId", "PartRequirementId", "QueuePosition", "PlateQuantity", "AssignmentMode",
                "Status", "FileRevisionHash", "FileRevisionNumber", "CreatedAtUtc", "TenantId")
            VALUES ({jobId}, {organization}, {requirementId}, 1024, 1, 'Unassigned', 'Queued', {part.RevisionHash}, 1, {now}, {tenant});
            """);
        await migrator.MigrateAsync(latest);
        db.ChangeTracker.Clear();
        var requirement = await db.PartRequirements.SingleAsync(r => r.Id == requirementId);
        requirement.LibraryPartId.Should().Be(part.Id);
        requirement.PartId.Should().Be(part.Id);
        requirement.PartName.Should().Be(part.Name);
        var line = await db.RunLines.SingleAsync(l => l.Id == lineId);
        line.TargetName.Should().Be(part.Name);
        line.RevisionMode.Should().Be(RevisionMode.Snapshot);
        var job = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
        job.FileRevisionHash.Should().Be(part.RevisionHash);
        job.ProductionSnapshotJson.Should().BeNull();
        job.SnapshotUnavailableAtUtc.Should().BeNull();
        job.SnapshotRecoveryReason.Should().Contain("awaiting verification");
        await scope.ServiceProvider.GetRequiredService<ProductionSnapshotReconciler>().ReconcileAsync(default);
        job.GetProductionPart()!.StoragePath.Should().Be(part.StoragePath);
        job.SnapshotRecoveryReason.Should().BeNull();
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
