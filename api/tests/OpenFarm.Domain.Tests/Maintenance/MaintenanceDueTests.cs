using FluentAssertions;
using OpenFarm.Domain.Maintenance;

namespace OpenFarm.Domain.Tests.Maintenance;

public class MaintenanceDueTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static MaintenanceTask Task(decimal? hours = null, int? days = null, int? prints = null, int warn = 80) =>
        MaintenanceTask.Create("Lubricate rails", null, hours, days, prints, warn);

    private static MaintenanceEnrollment Enrollment(decimal carryHours = 0, int carryPrints = 0) =>
        MaintenanceEnrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Start, carryHours, carryPrints, Start);

    [Fact]
    public void Hours_Alone()
    {
        var due = MaintenanceDue.Evaluate(Task(hours: 100), Enrollment(), new MaintenanceUsage(40, 0), Start);

        due.Should().Be(new MaintenanceDueResult(MaintenanceDueState.Ok, MaintenanceTrigger.Hours, 40, 100, 60));
    }

    [Fact]
    public void Days_Alone_UseElapsedTime()
    {
        var due = MaintenanceDue.Evaluate(Task(days: 10), Enrollment(), new MaintenanceUsage(999, 999), Start.AddDays(4.5));

        due.Should().Be(new MaintenanceDueResult(MaintenanceDueState.Ok, MaintenanceTrigger.Days, 4.5m, 10, 5.5m));
    }

    [Fact]
    public void Prints_Alone()
    {
        var due = MaintenanceDue.Evaluate(Task(prints: 50), Enrollment(), new MaintenanceUsage(0, 50), Start);

        due.Should().Be(new MaintenanceDueResult(MaintenanceDueState.Overdue, MaintenanceTrigger.Prints, 50, 50, 0));
    }

    [Fact]
    public void WhicheverComesFirst_DrivesTheResult()
    {
        // hours 10/100 = 0.1, days 9/10 = 0.9, prints 20/50 = 0.4
        var due = MaintenanceDue.Evaluate(Task(100, 10, 50), Enrollment(), new MaintenanceUsage(10, 20), Start.AddDays(9));

        due.State.Should().Be(MaintenanceDueState.DueSoon);
        due.DrivingTrigger.Should().Be(MaintenanceTrigger.Days);
    }

    [Fact]
    public void Carries_AddToMeasuredUsage()
    {
        var due = MaintenanceDue.Evaluate(Task(hours: 100, prints: 50), Enrollment(carryHours: 30, carryPrints: 45), new MaintenanceUsage(10, 3), Start);

        due.Should().Be(new MaintenanceDueResult(MaintenanceDueState.DueSoon, MaintenanceTrigger.Prints, 48, 50, 2));
    }

    [Theory]
    [InlineData(79.9, MaintenanceDueState.Ok)]
    [InlineData(80, MaintenanceDueState.DueSoon)]
    [InlineData(99.9, MaintenanceDueState.DueSoon)]
    [InlineData(100, MaintenanceDueState.Overdue)]
    public void WarnAndOverdueBoundaries(double hours, MaintenanceDueState expected)
    {
        var due = MaintenanceDue.Evaluate(Task(hours: 100, warn: 80), Enrollment(), new MaintenanceUsage((decimal)hours, 0), Start);

        due.State.Should().Be(expected);
    }

    [Fact]
    public void Remaining_IsNegativeWhenOverdue()
    {
        var due = MaintenanceDue.Evaluate(Task(hours: 100), Enrollment(), new MaintenanceUsage(112, 0), Start);

        due.State.Should().Be(MaintenanceDueState.Overdue);
        due.Remaining.Should().Be(-12);
    }
}
