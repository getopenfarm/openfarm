using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Tests.Persistence;

public class MigrationModelTests
{
    [Fact]
    public void Migrations_MatchTheCurrentModel()
    {
        using var db = new OpenFarmDbContextFactory().CreateDbContext([]);
        db.Database.HasPendingModelChanges().Should().BeFalse();
        // The history starts at the one InitialCreate; each later schema change adds a single migration of its own.
        db.Database.GetMigrations().First().Should().EndWith("_InitialCreate");
    }
}
