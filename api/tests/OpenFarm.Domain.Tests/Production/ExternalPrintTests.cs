using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class ExternalPrintTests
{
    private static readonly Guid SpoolA = Guid.NewGuid();
    private static readonly Guid SpoolB = Guid.NewGuid();

    private static ExternalPrint Start(int tools) => ExternalPrint.Start(Guid.NewGuid(), "benchy",
        Enumerable.Range(0, tools).Select(i => new ExternalFilamentSpec(i, "PLA", null, 10, null)));

    [Fact]
    public void Close_BindsTheFirstTraySeen_ForASingleTool()
    {
        var print = Start(1);
        print.ObserveTray(1, SpoolA);
        print.ObserveTray(2, SpoolB);

        print.MarkCompleted();

        print.Filaments[0].SpoolId.Should().Be(SpoolA);
    }

    [Fact]
    public void Close_DoesNotPairSeveralToolsByOrderOfAppearance()
    {
        var print = Start(2);
        print.ObserveTray(3, SpoolB);
        print.ObserveTray(0, SpoolA);

        print.MarkFailed();

        print.Filaments.Should().OnlyContain(f => f.SpoolId == null);
    }

    [Fact]
    public void BindTools_BindsByToolIndex_AndSurvivesTheClose()
    {
        var print = Start(2);

        print.MarkCompleted();
        print.BindTools(new Dictionary<int, Guid> { [1] = SpoolA });

        print.Filaments.Select(f => f.SpoolId).Should().Equal(null, SpoolA);
    }

    [Fact]
    public void BindTools_NeverReplacesABoundSpool()
    {
        var print = Start(1);
        print.ObserveTray(1, SpoolA);
        print.MarkCompleted();

        print.BindTools(new Dictionary<int, Guid> { [0] = SpoolB });

        print.Filaments[0].SpoolId.Should().Be(SpoolA);
    }

    [Fact]
    public void Close_LeavesToolsUnbound_WhenTrayCountDoesNotMatch()
    {
        var print = Start(2);
        print.ObserveTray(0, SpoolA);

        print.MarkCompleted();

        print.Filaments.Should().OnlyContain(f => f.SpoolId == null);
    }

    [Fact]
    public void ObserveTray_IgnoresARepeatedTray_AndAnyTrayAfterTheEnd()
    {
        var print = Start(1);
        print.ObserveTray(0, SpoolA).Should().BeTrue();
        print.ObserveTray(0, SpoolA).Should().BeFalse();

        print.MarkCompleted();

        print.ObserveTray(1, SpoolB).Should().BeFalse();
    }

    [Fact]
    public void Start_BeginsPrinting_AsAnExternalPrint()
    {
        var print = Start(1);

        print.Status.Should().Be(PrintJobStatus.Printing);
        print.Source.Should().Be(PrintSource.External);
        print.StartedAtUtc.Should().NotBeNull();
        print.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Close_RecordsTheEndAndDuration_AfterAPause()
    {
        var print = Start(1);

        print.MarkPaused();
        print.MarkResumed();
        print.MarkCompleted();

        print.Status.Should().Be(PrintJobStatus.Completed);
        print.EndedAtUtc.Should().NotBeNull();
        print.ActualDurationMinutes.Should().Be(0);
    }

    [Fact]
    public void AClosedPrint_CannotBeRestarted()
    {
        var print = Start(1);
        print.MarkCompleted();

        var restart = print.MarkResumed;

        restart.Should().Throw<Exception>();
    }

    [Fact]
    public void ObserveTray_FillsInASpoolThatWasUnknownWhenTheTrayFirstAppeared()
    {
        var print = Start(1);
        print.ObserveTray(254, null).Should().BeTrue();
        print.WantsSpoolFor(254).Should().BeTrue();

        print.ObserveTray(254, SpoolA).Should().BeTrue("the tray was seen without a spool, and now one is known");

        print.WantsSpoolFor(254).Should().BeFalse();
        print.ObservedSpoolIds.Should().Equal(SpoolA);
        print.MarkCompleted();
        print.Filaments[0].SpoolId.Should().Be(SpoolA);
    }

    [Fact]
    public void ObserveTray_NeverReplacesASpoolAlreadyKnownForTheTray()
    {
        var print = Start(1);
        print.ObserveTray(1, SpoolA);

        print.ObserveTray(1, SpoolB).Should().BeFalse();

        print.ObservedSpoolIds.Should().Equal(SpoolA);
    }

    [Fact]
    public void WantsSpoolFor_IsTrueForANewTray_AndFalseOnceTheRunHasEnded()
    {
        var print = Start(1);
        print.WantsSpoolFor(3).Should().BeTrue();

        print.MarkCompleted();

        print.WantsSpoolFor(3).Should().BeFalse();
    }
}
