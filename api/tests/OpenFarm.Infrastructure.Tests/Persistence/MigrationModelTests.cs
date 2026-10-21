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
        db.Database.GetMigrations().Should().ContainSingle().Which.Should().EndWith("_InitialCreate");
    }
}
