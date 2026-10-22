using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class PrintFilamentCostTests
{
    private static readonly Guid SpoolId = Guid.NewGuid();

    private static PrintJob Job() => PrintJob.Create(
        "benchy", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null, "hash", 1, [new JobFilamentRequirementSpec("PLA", null)]);

    private static ResolvedFilamentPrice Price(decimal perKg, FilamentPriceSource source = FilamentPriceSource.Spool) => new(perKg, source);

    [Fact]
    public void ALineStartsUncosted()
    {
        var line = Job().Filaments.Single();

        line.PricePerKg.Should().BeNull();
        line.PriceSource.Should().BeNull();
        line.Cost.Should().BeNull();
    }

    [Fact]
    public void Cost_IsTheGramsDeductedTimesThePricePerKilogramOverAThousand()
    {
        var job = Job();
        var line = job.Filaments.Single();

        job.RecordFilamentConsumption(line.Id, SpoolId, 50m);
        job.SnapshotFilamentPrice(line.Id, Price(20m));

        line.PricePerKg.Should().Be(20m);
        line.PriceSource.Should().Be(FilamentPriceSource.Spool);
        line.Cost.Should().Be(1.00m);
    }

    [Fact]
    public void Cost_IsUnknown_WithAPriceButNothingDeducted()
    {
        var job = Job();
        var line = job.Filaments.Single();

        job.SnapshotFilamentPrice(line.Id, Price(20m));

        line.Cost.Should().BeNull();
    }

    [Fact]
    public void Cost_IsUnknown_WithGramsButNoPrice()
    {
        var job = Job();
        var line = job.Filaments.Single();

        job.RecordFilamentConsumption(line.Id, SpoolId, 50m);

        line.Cost.Should().BeNull();
    }

    [Fact]
    public void AZeroPrice_GivesAZeroCost_NotAnUnknownOne()
    {
        var job = Job();
        var line = job.Filaments.Single();

        job.RecordFilamentConsumption(line.Id, SpoolId, 50m);
        job.SnapshotFilamentPrice(line.Id, Price(0m));

        line.Cost.Should().Be(0m);
    }

    [Fact]
    public void ThePriceIsSnapshottedOnce_ALaterPriceDoesNotReplaceIt()
    {
        var job = Job();
        var line = job.Filaments.Single();
        job.RecordFilamentConsumption(line.Id, SpoolId, 50m);

        job.SnapshotFilamentPrice(line.Id, Price(20m));
        job.SnapshotFilamentPrice(line.Id, Price(99m, FilamentPriceSource.Variant));

        line.PricePerKg.Should().Be(20m);
        line.PriceSource.Should().Be(FilamentPriceSource.Spool);
        line.Cost.Should().Be(1.00m);
    }

    [Fact]
    public void AnUnpricedLine_CanStillGainAPriceLater()
    {
        var job = Job();
        var line = job.Filaments.Single();
        job.RecordFilamentConsumption(line.Id, SpoolId, 50m);

        job.SnapshotFilamentPrice(line.Id, null);
        line.Cost.Should().BeNull();

        job.SnapshotFilamentPrice(line.Id, Price(30m, FilamentPriceSource.Variant));

        line.PriceSource.Should().Be(FilamentPriceSource.Variant);
        line.Cost.Should().Be(1.50m);
    }

    [Fact]
    public void LaterDeductions_AreCostedAtTheSnapshottedPrice()
    {
        var job = Job();
        var line = job.Filaments.Single();
        job.RecordFilamentConsumption(line.Id, SpoolId, 20m);
        job.SnapshotFilamentPrice(line.Id, Price(20m));
        line.Cost.Should().Be(0.40m);

        job.RecordFilamentConsumption(line.Id, SpoolId, 50m);

        line.Cost.Should().Be(1.00m);
    }
}
