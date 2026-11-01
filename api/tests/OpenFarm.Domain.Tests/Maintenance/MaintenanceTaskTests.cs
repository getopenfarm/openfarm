using FluentAssertions;
using OpenFarm.Domain.Maintenance;

namespace OpenFarm.Domain.Tests.Maintenance;

public class MaintenanceTaskTests
{
    [Fact]
    public void Create_TrimsAndKeepsIntervals()
    {
        var task = MaintenanceTask.Create("  Clean rods ", " wipe ", 200, 30, null, 80);

        task.Name.Should().Be("Clean rods");
        task.Notes.Should().Be("wipe");
        task.EveryPrintHours.Should().Be(200);
        task.EveryDays.Should().Be(30);
        task.EveryPrints.Should().BeNull();
        task.WarnAtPercent.Should().Be(80);
    }

    [Fact]
    public void Create_WithoutAnyInterval_Throws()
    {
        var act = () => MaintenanceTask.Create("Clean rods", null, null, null, null, 80);

        act.Should().Throw<MaintenanceException>();
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, null, -1)]
    public void Create_WithNonPositiveInterval_Throws(int? hours, int? days, int? prints)
    {
        var act = () => MaintenanceTask.Create("Clean rods", null, hours, days, prints, 80);

        act.Should().Throw<MaintenanceException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Create_WithWarnOutOfRange_Throws(int warn)
    {
        var act = () => MaintenanceTask.Create("Clean rods", null, 10, null, null, warn);

        act.Should().Throw<MaintenanceException>();
    }

    [Fact]
    public void Create_WithBlankOrLongName_Throws()
    {
        var blank = () => MaintenanceTask.Create(" ", null, 10, null, null, 80);
        var tooLong = () => MaintenanceTask.Create(new string('x', MaintenanceTask.MaxNameLength + 1), null, 10, null, null, 80);

        blank.Should().Throw<MaintenanceException>();
        tooLong.Should().Throw<MaintenanceException>();
    }

    [Fact]
    public void Edit_ReplacesDefinition()
    {
        var task = MaintenanceTask.Create("Clean rods", null, 200, null, null, 80);

        task.Edit("Clean rods", "note", null, null, 50, 90);

        task.EveryPrintHours.Should().BeNull();
        task.EveryPrints.Should().Be(50);
        task.WarnAtPercent.Should().Be(90);
        task.Notes.Should().Be("note");
    }
}
