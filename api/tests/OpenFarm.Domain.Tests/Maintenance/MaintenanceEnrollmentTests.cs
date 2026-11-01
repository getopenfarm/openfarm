using FluentAssertions;
using OpenFarm.Domain.Maintenance;

namespace OpenFarm.Domain.Tests.Maintenance;

public class MaintenanceEnrollmentTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Start.AddDays(40);
    private static readonly Guid UserId = Guid.NewGuid();

    private static MaintenanceEnrollment Enrollment() =>
        MaintenanceEnrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Start, 12.5m, 3, Now);

    private static MaintenanceLog Complete(MaintenanceEnrollment enrollment, DateTimeOffset performedAt) =>
        enrollment.Complete(performedAt, Now, "Clean rods", "P1S-01", new MaintenanceUsage(100, 20), UserId, " Bob ", " done ", 4.5m);

    [Fact]
    public void Create_WithNegativeCarry_Throws()
    {
        var act = () => MaintenanceEnrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Start, -1, 0, Now);

        act.Should().Throw<MaintenanceException>();
    }

    [Fact]
    public void Create_WithFutureCycleStart_Throws()
    {
        var act = () => MaintenanceEnrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(1), 0, 0, Now);

        act.Should().Throw<MaintenanceException>();
    }

    [Fact]
    public void Rebaseline_StoresOffsetDatesAsUtc()
    {
        var enrollment = Enrollment();

        enrollment.Rebaseline(new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.FromHours(8)), 0, 0, Now);

        enrollment.CycleStartedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        enrollment.CycleStartedAtUtc.Should().Be(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Complete_ResetsCycleAndReturnsLog()
    {
        var enrollment = Enrollment();
        var performedAt = Start.AddDays(30);

        var log = Complete(enrollment, performedAt);

        enrollment.CycleStartedAtUtc.Should().Be(performedAt);
        enrollment.CarryHours.Should().Be(0);
        enrollment.CarryPrints.Should().Be(0);
        log.EnrollmentId.Should().Be(enrollment.Id);
        log.TaskId.Should().Be(enrollment.TaskId);
        log.PrinterId.Should().Be(enrollment.PrinterId);
        log.TaskName.Should().Be("Clean rods");
        log.PrinterName.Should().Be("P1S-01");
        log.PerformedAtUtc.Should().Be(performedAt);
        log.PerformedByUserId.Should().Be(UserId);
        log.PerformedByNote.Should().Be("Bob");
        log.Notes.Should().Be("done");
        log.Cost.Should().Be(4.5m);
        log.HoursAtCompletion.Should().Be(112.5m);
        log.PrintsAtCompletion.Should().Be(23);
    }

    [Fact]
    public void Complete_InTheFuture_Throws()
    {
        var act = () => Complete(Enrollment(), Now.AddSeconds(1));

        act.Should().Throw<MaintenanceException>();
    }

    [Fact]
    public void Complete_BeforeCycleStart_Throws()
    {
        var act = () => Complete(Enrollment(), Start.AddSeconds(-1));

        act.Should().Throw<MaintenanceException>();
    }

    [Fact]
    public void Complete_WithNegativeCost_Throws()
    {
        var act = () => Enrollment().Complete(Now, Now, "t", "p", new MaintenanceUsage(0, 0), UserId, null, null, -1);

        act.Should().Throw<MaintenanceException>();
    }

    [Fact]
    public void Rebaseline_SetsCycleStartAndCarries()
    {
        var enrollment = Enrollment();

        enrollment.Rebaseline(Start.AddDays(-10), 50, 7, Now);

        enrollment.CycleStartedAtUtc.Should().Be(Start.AddDays(-10));
        enrollment.CarryHours.Should().Be(50);
        enrollment.CarryPrints.Should().Be(7);
    }

    [Fact]
    public void Rebaseline_InTheFutureOrWithNegativeCarry_Throws()
    {
        var future = () => Enrollment().Rebaseline(Now.AddDays(1), 0, 0, Now);
        var negative = () => Enrollment().Rebaseline(Start, 0, -1, Now);

        future.Should().Throw<MaintenanceException>();
        negative.Should().Throw<MaintenanceException>();
    }
}
