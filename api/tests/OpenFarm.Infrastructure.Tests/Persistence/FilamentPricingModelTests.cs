using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Tests.Persistence;

public class FilamentPricingModelTests
{
    [Fact]
    public void PricingMigration_TargetMatchesCurrentModel()
    {
        using var db = new OpenFarmDbContextFactory().CreateDbContext([]);
        db.Database.HasPendingModelChanges().Should().BeFalse();
        db.Database.GetMigrations().Should().Contain("20261019090000_AddFilamentPrices");
    }
}
