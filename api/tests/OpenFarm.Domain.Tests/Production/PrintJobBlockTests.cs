using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

// A job whose attempt failed after a printer matched it says why and is left alone for a growing pause, so a failure
// that has not been fixed is not retried (and re-sliced) on every telemetry tick.
public class PrintJobBlockTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    private static PrintJob Job() => PrintJob.Create(
        "benchy", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null, "hash", 1, [new JobFilamentRequirementSpec("PLA", null)]);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    [InlineData(40, 5)]
    public void RetryDelay_DoublesFromAMinuteAndStopsAtFiveMinutes(int failures, int expectedMinutes) =>
        PrintJob.RetryDelay(failures).Should().Be(TimeSpan.FromMinutes(expectedMinutes));

    [Fact]
    public void RecordBlock_KeepsTheReasonAndCountsTheFailure()
    {
        var job = Job();

        job.RecordBlock("Slicer crashed.", Start);
        job.RecordBlock("Slicer crashed again.", Start.AddMinutes(2));

        job.LastBlockReason.Should().Be("Slicer crashed again.");
        job.LastBlockedAtUtc.Should().Be(Start.AddMinutes(2));
        job.BlockFailureCount.Should().Be(2);
    }

    [Fact]
    public void IsInRetryBackoff_OnlyUntilTheDelayAfterTheLastFailureHasPassed()
    {
        var job = Job();
        job.IsInRetryBackoff(Start).Should().BeFalse("a job that never failed is never delayed");

        job.RecordBlock("Boom.", Start);
        job.IsInRetryBackoff(Start.AddSeconds(59)).Should().BeTrue();
        job.IsInRetryBackoff(Start.AddSeconds(60)).Should().BeFalse();

        job.RecordBlock("Boom again.", Start.AddMinutes(1));
        job.IsInRetryBackoff(Start.AddMinutes(2).AddSeconds(59)).Should().BeTrue();
        job.IsInRetryBackoff(Start.AddMinutes(3)).Should().BeFalse();
    }

    [Fact]
    public void ClearBlock_ForgetsTheFailureAndTheDelay()
    {
        var job = Job();
        job.RecordBlock("Boom.", Start);

        job.ClearBlock();

        job.LastBlockReason.Should().BeNull();
        job.BlockFailureCount.Should().Be(0);
        job.IsInRetryBackoff(Start).Should().BeFalse();
    }

    [Fact]
    public void OperatorEditsThatChangeWhatCouldBeSent_ClearTheBlock()
    {
        var job = Job();
        job.RecordBlock("Boom.", Start);
        job.Pin(Guid.NewGuid());
        job.BlockFailureCount.Should().Be(0);

        job.RecordBlock("Boom.", Start);
        job.Unpin();
        job.BlockFailureCount.Should().Be(0);

        job.RecordBlock("Boom.", Start);
        job.SetFilamentRequirements([new JobFilamentRequirementSpec("PETG", null)]);
        job.BlockFailureCount.Should().Be(0);
    }
}

// A snapshot that can no longer be read is "no snapshot": the job needs recovery, nothing else is affected.
public class PrintJobUnreadableSnapshotTests
{
    private static PrintJob JobWithSnapshot(string? json)
    {
        var job = PrintJob.Create("benchy", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null, "hash", 1, [new JobFilamentRequirementSpec("PLA", null)]);
        typeof(PrintJob).GetProperty(nameof(PrintJob.ProductionSnapshotJson))!.SetValue(job, json);
        return job;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"text\"")]
    public void GetProductionPart_WithNothingReadable_IsNull(string? json) =>
        JobWithSnapshot(json).GetProductionPart().Should().BeNull();
}
