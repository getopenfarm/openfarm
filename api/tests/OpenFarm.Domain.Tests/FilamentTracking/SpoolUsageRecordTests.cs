using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class SpoolUsageRecordTests
{
    private static readonly Guid SpoolId = Guid.NewGuid();

    [Fact]
    public void Create_ForAJobOrManualUse_KeepsGramsRemainingAfterAndPrint()
    {
        var printId = Guid.NewGuid();

        var record = SpoolUsageRecord.Create(SpoolId, 12.5m, printId, SpoolUsageSource.Job, remainingAfterGrams: 987.5m);

        record.SpoolId.Should().Be(SpoolId);
        record.GramsConsumed.Should().Be(12.5m);
        record.RemainingAfterGrams.Should().Be(987.5m);
        record.JobId.Should().Be(printId);
        record.Source.Should().Be(SpoolUsageSource.Job);
        record.Reason.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveGrams_Throws(double grams)
    {
        var create = () => SpoolUsageRecord.Create(SpoolId, (decimal)grams, null, SpoolUsageSource.Manual, 100m);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithCorrectionSource_Throws()
    {
        var create = () => SpoolUsageRecord.Create(SpoolId, 5m, null, SpoolUsageSource.Correction, 95m);

        create.Should().Throw<ArgumentException>().WithMessage("*Correction*");
    }

    [Fact]
    public void Correction_ThatLowersTheWeight_RecordsTheGramsRemoved()
    {
        var record = SpoolUsageRecord.Correction(SpoolId, beforeGrams: 800m, afterGrams: 750m, reason: "weighed on scale");

        record.GramsConsumed.Should().Be(50m);
        record.RemainingAfterGrams.Should().Be(750m);
        record.Source.Should().Be(SpoolUsageSource.Correction);
        record.JobId.Should().BeNull();
        record.Reason.Should().Be("weighed on scale");
    }

    [Fact]
    public void Correction_ThatRaisesTheWeight_RecordsNegativeGramsRemoved()
    {
        var record = SpoolUsageRecord.Correction(SpoolId, beforeGrams: 750m, afterGrams: 780m, reason: null);

        record.GramsConsumed.Should().Be(-30m);
        record.RemainingAfterGrams.Should().Be(780m);
    }

    [Fact]
    public void Correction_WithTheSameWeight_Throws()
    {
        var correct = () => SpoolUsageRecord.Correction(SpoolId, 500m, 500m, null);

        correct.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("  weighed on scale  ", "weighed on scale")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Correction_TrimsTheReason_AndTurnsBlankIntoNull(string? reason, string? expected)
    {
        var record = SpoolUsageRecord.Correction(SpoolId, 500m, 400m, reason);

        record.Reason.Should().Be(expected);
    }

    [Fact]
    public void Correction_WithAReasonLongerThanTheLimit_Throws()
    {
        var tooLong = new string('x', SpoolUsageRecord.MaxReasonLength + 1);

        var correct = () => SpoolUsageRecord.Correction(SpoolId, 500m, 400m, tooLong);

        correct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Correction_WithAReasonAtTheLimit_IsKept()
    {
        var atLimit = new string('x', SpoolUsageRecord.MaxReasonLength);

        var record = SpoolUsageRecord.Correction(SpoolId, 500m, 400m, atLimit);

        record.Reason.Should().HaveLength(SpoolUsageRecord.MaxReasonLength);
    }
}
