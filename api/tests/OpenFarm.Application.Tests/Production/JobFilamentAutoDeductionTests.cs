using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class JobFilamentAutoDeductionTests
{
    private readonly InMemorySpoolUsageRecordRepository _usage = new();
    private readonly RecordingActivityLogWriter _log = new();

    private JobFilamentAutoDeduction Create(params Spool[] spools) =>
        new(new InMemorySpoolRepository(spools), _usage, new NoopContinuationResolver(), _log);

    private static Spool UsingSpool(decimal remaining = 1000)
    {
        var spool = Spool.Create(Guid.NewGuid(), null, 1000, remaining);
        spool.LoadOntoFeedSlot(Guid.NewGuid());
        return spool;
    }

    private static PrintJob JobWith(params (string Material, string Color)[] lines)
    {
        var job = PrintJob.Create("Test part", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null, "h", 1,
            lines.Select(l => new JobFilamentRequirementSpec(l.Material, l.Color)).ToList());
        return job;
    }

    private static JobProgress Completed => new(PrintJobStatus.Completed);

    private static JobProgress FailedAt(int? layer = null, int? total = null, int? percent = null) =>
        new(PrintJobStatus.Failed, layer, total, percent);

    private static PrintFilament LineWith(FilamentUsageCurve? curve)
    {
        var job = JobWith(("PLA", "#FF0000"));
        job.BindFilamentUsage([(Guid.NewGuid(), 10m, curve)]);
        return job.Filaments[0];
    }

    [Fact]
    public void Fraction_FinishedPrint_IsAll() =>
        JobFilamentAutoDeduction.Fraction(LineWith(null), Completed).Should().Be(1m);

    [Theory]
    [InlineData(40, 100, null, 0.4)]
    [InlineData(120, 100, null, 1)]
    [InlineData(null, null, 25, 0.25)]
    public void Fraction_WithoutACurve_IsAnEvenSpreadOverLayersThenPercent(int? layer, int? total, int? percent, double expected) =>
        JobFilamentAutoDeduction.Fraction(LineWith(null), FailedAt(layer, total, percent)).Should().Be((decimal)expected);

    [Fact]
    public void Fraction_WithACurve_FollowsWhatThatColourUsedByTheLayer()
    {
        var early = LineWith(FilamentUsageCurve.Create([[10, 20, 20, 20]]));
        var late = LineWith(FilamentUsageCurve.Create([[0, 0, 5, 10]]));

        JobFilamentAutoDeduction.Fraction(early, FailedAt(layer: 3, total: 4)).Should().Be(1m);
        JobFilamentAutoDeduction.Fraction(late, FailedAt(layer: 3, total: 4)).Should().Be(0.25m);
    }

    [Fact]
    public void Fraction_FailedWithoutProgress_IsUnknown() =>
        JobFilamentAutoDeduction.Fraction(LineWith(null), FailedAt()).Should().BeNull();

    [Fact]
    public async Task Completed_MultiColour_DeductsEachLineFromItsOwnSpool()
    {
        var red = UsingSpool();
        var white = UsingSpool();
        var job = JobWith(("PLA", "#FF0000"), ("PLA", "#FFFFFF"));
        job.BindFilamentUsage([(red.Id, 12.5m, null), (white.Id, 4m, null)]);

        await Create(red, white).ApplyAsync(job, Completed, CancellationToken.None);

        red.RemainingWeightGrams.Should().Be(987.5m);
        white.RemainingWeightGrams.Should().Be(996m);
        job.Filaments.Select(l => l.GramsConsumed).Should().Equal(12.5m, 4m);
        _usage.Records.Should().OnlyContain(r => r.Source == SpoolUsageSource.Job && r.JobId == job.Id);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_DeductsProportionally_ThenRepeatDoesNotDeductTwice()
    {
        var spool = UsingSpool();
        var job = JobWith(("PLA", "#FF0000"));
        job.BindFilamentUsage([(spool.Id, 20m, null)]);
        var deduction = Create(spool);

        await deduction.ApplyAsync(job, FailedAt(percent: 40), CancellationToken.None);
        await deduction.ApplyAsync(job, FailedAt(percent: 40), CancellationToken.None);

        spool.RemainingWeightGrams.Should().Be(992m);
        _usage.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task Failed_MultiColour_EachColourDeductsWhatItUsedByTheLayerReached()
    {
        var red = UsingSpool();
        var white = UsingSpool();
        var job = JobWith(("PLA", "#FF0000"), ("PLA", "#FFFFFF"));
        // Red prints the first two layers, white the last two; the print stops while on layer 3 of 4.
        job.BindFilamentUsage([
            (red.Id, 20m, FilamentUsageCurve.Create([[10, 20, 20, 20]])),
            (white.Id, 10m, FilamentUsageCurve.Create([[0, 0, 5, 10]]))]);

        await Create(red, white).ApplyAsync(job, FailedAt(layer: 3, total: 4), CancellationToken.None);

        red.RemainingWeightGrams.Should().Be(980m, "red was fully used before layer 3");
        white.RemainingWeightGrams.Should().Be(997.5m, "white had barely started");
    }

    [Fact]
    public async Task UnboundOrUnestimatedLine_IsSkippedAndLogged()
    {
        var spool = UsingSpool();
        var job = JobWith(("PLA", "#FF0000"), ("PLA", "#FFFFFF"));
        job.BindFilamentUsage([(spool.Id, null, null), (null, 5m, null)]);

        await Create(spool).ApplyAsync(job, Completed, CancellationToken.None);

        spool.RemainingWeightGrams.Should().Be(1000m);
        _usage.Records.Should().BeEmpty();
        _log.Entries.Should().ContainSingle(e => e.EventType == "PrintJobFilamentDeductionSkipped");
    }

    [Fact]
    public async Task UnknownFraction_DeductsNothingAndLogs()
    {
        var spool = UsingSpool();
        var job = JobWith(("PLA", "#FF0000"));
        job.BindFilamentUsage([(spool.Id, 20m, null)]);

        await Create(spool).ApplyAsync(job, FailedAt(), CancellationToken.None);

        spool.RemainingWeightGrams.Should().Be(1000m);
        _log.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task SpoolNoLongerUsing_IsSkippedInsteadOfThrowing()
    {
        var spool = UsingSpool();
        spool.ReturnToStorage();
        var job = JobWith(("PLA", "#FF0000"));
        job.BindFilamentUsage([(spool.Id, 20m, null)]);

        var act = () => Create(spool).ApplyAsync(job, Completed, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _log.Entries.Should().ContainSingle();
    }

    private static PrintJob ClosedJob(JobProgress outcome, params (string Material, string Color)[] lines)
    {
        var job = JobWith(lines);
        job.MarkStarted();
        if (outcome.Outcome == PrintJobStatus.Completed) job.MarkCompleted();
        else job.MarkFailed();
        return job;
    }

    [Fact]
    public async Task EachSkippedLine_RecordsItsReason()
    {
        var spool = UsingSpool();
        var returned = UsingSpool();
        returned.ReturnToStorage();
        var job = JobWith(("PLA", "#000000"), ("PLA", "#111111"), ("PLA", "#222222"));
        job.BindFilamentUsage([(spool.Id, null, null), (null, 5m, null), (returned.Id, 5m, null)]);

        await Create(spool, returned).ApplyAsync(job, Completed, CancellationToken.None);

        job.Filaments.Select(l => l.SkippedReason).Should().Equal(
            FilamentSkipReason.NoEstimate, FilamentSkipReason.NoSpool, FilamentSkipReason.SpoolNotLoaded);
        job.NeedsFilamentFollowUp.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownFraction_IsNoProgress_EvenWithoutASpool()
    {
        // A spool choice cannot fix this, so it must not be reported as the fixable NoSpool.
        var job = JobWith(("PLA", "#FF0000"));
        job.BindFilamentUsage([(null, 20m, null)]);

        await Create().ApplyAsync(job, FailedAt(), CancellationToken.None);

        job.Filaments[0].SkippedReason.Should().Be(FilamentSkipReason.NoProgress);
    }

    [Fact]
    public async Task LogSkippedFalse_StillRecordsTheReasonButWritesNoLog()
    {
        var job = JobWith(("PLA", "#FF0000"));
        job.BindFilamentUsage([(null, 20m, null)]);

        await Create().ApplyAsync(job, Completed, CancellationToken.None, logSkipped: false);

        job.Filaments[0].SkippedReason.Should().Be(FilamentSkipReason.NoSpool);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AssigningASpoolLater_DeductsFromThenOnAndClearsTheReason()
    {
        var spool = UsingSpool();
        var job = ClosedJob(Completed, ("PLA", "#FF0000"));
        job.BindFilamentUsage([(null, 20m, null)]);
        var deduction = Create(spool);
        await deduction.ApplyAsync(job, Completed, CancellationToken.None);

        job.BindLineSpool(job.Filaments[0].Id, spool.Id);
        await deduction.ApplyAsync(job, Completed, CancellationToken.None, logSkipped: false);
        await deduction.ApplyAsync(job, Completed, CancellationToken.None, logSkipped: false);

        spool.RemainingWeightGrams.Should().Be(980m);
        job.Filaments[0].SkippedReason.Should().BeNull();
        job.NeedsFilamentFollowUp.Should().BeFalse();
        _usage.Records.Should().ContainSingle(r => r.JobId == job.Id && r.GramsConsumed == 20m);
    }

    [Fact]
    public async Task AssigningASpoolToAFailedPrint_UsesTheStoppedProgress()
    {
        var spool = UsingSpool();
        var job = ClosedJob(FailedAt(), ("PLA", "#FF0000"));
        job.BindFilamentUsage([(null, 20m, null)]);
        job.RecordStopProgress(null, null, 40);
        var deduction = Create(spool);
        await deduction.ApplyAsync(job, FailedAt(percent: 40), CancellationToken.None);

        job.BindLineSpool(job.Filaments[0].Id, spool.Id);
        await deduction.ApplyAsync(job, new JobProgress(job.Status, job.StopLayer, job.StopTotalLayers, job.StopPercent),
            CancellationToken.None, logSkipped: false);

        spool.RemainingWeightGrams.Should().Be(992m);
    }

    [Fact]
    public async Task AssigningASpoolToAPrintThatUsedNothing_SettlesTheLineWithZero()
    {
        var spool = UsingSpool();
        var job = ClosedJob(FailedAt(), ("PLA", "#FF0000"));
        job.BindFilamentUsage([(null, 20m, null)]);
        var deduction = Create(spool);
        await deduction.ApplyAsync(job, FailedAt(percent: 0), CancellationToken.None);

        job.BindLineSpool(job.Filaments[0].Id, spool.Id);
        await deduction.ApplyAsync(job, FailedAt(percent: 0), CancellationToken.None, logSkipped: false);

        job.Filaments[0].GramsConsumed.Should().Be(0m);
        job.Filaments[0].SkippedReason.Should().BeNull();
        spool.RemainingWeightGrams.Should().Be(1000m);
        _usage.Records.Should().BeEmpty();
    }
}
