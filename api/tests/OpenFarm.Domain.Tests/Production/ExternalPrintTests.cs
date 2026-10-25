using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class ExternalPrintTests
{
    private static readonly Guid SpoolA = Guid.NewGuid();
    private static readonly Guid SpoolB = Guid.NewGuid();

    private static FeedSlotRef Slot(int position) => new(FeedUnitKindCatalog.MultiMaterialUnit, 0, position);

    private static readonly FeedSlotRef ExternalPort = new(FeedUnitKindCatalog.ExternalSpool, 0, 0);

    private static ExternalPrint Start(int tools) => ExternalPrint.Start(Guid.NewGuid(), "benchy",
        Enumerable.Range(0, tools).Select(i => new ExternalFilamentSpec(i, "PLA", null, 10, null)));

    [Fact]
    public void APrintSeenWithoutItsFile_GetsOneLineOfUnknownGrams()
    {
        var print = ExternalPrint.Start(Guid.NewGuid(), "benchy");
        print.Filaments.Should().BeEmpty();

        print.AttachFilaments(null);
        print.AttachFilaments([new ExternalFilamentSpec(0, "PLA", null, 10, null)]);

        // Deduction skips a line with no grams as NoEstimate, so the print reaches Action required instead of vanishing.
        print.Filaments.Should().ContainSingle().Which.EstimatedGrams.Should().BeNull();
    }

    [Fact]
    public void AttachFilaments_GivesEachToolItsLine_InToolOrder()
    {
        var print = ExternalPrint.Start(Guid.NewGuid(), "benchy");

        print.AttachFilaments([new ExternalFilamentSpec(1, "PETG", null, 5, null), new ExternalFilamentSpec(0, "PLA", null, 10, null)]);

        print.Filaments.Select(f => (f.ToolIndex, f.EstimatedGrams)).Should().Equal((0, 10m), (1, 5m));
    }

    [Fact]
    public void AnInferredEnd_IsKept_ButNeverBeforeTheStart()
    {
        var print = Start(1);
        var started = print.StartedAtUtc!.Value;

        print.MarkCompleted(started.AddMinutes(-5));

        print.EndedAtUtc.Should().Be(started);
        print.ActualDurationMinutes.Should().Be(0);

        var later = Start(1);
        later.MarkFailed(endedAtUtc: later.StartedAtUtc!.Value.AddMinutes(90));
        later.ActualDurationMinutes.Should().Be(90);
    }

    [Fact]
    public void Close_BindsTheFirstTraySeen_ForASingleTool()
    {
        var print = Start(1);
        print.ObserveSlot(Slot(1), SpoolA);
        print.ObserveSlot(Slot(2), SpoolB);

        print.MarkCompleted();

        print.Filaments[0].SpoolId.Should().Be(SpoolA);
    }

    [Fact]
    public void Close_DoesNotPairSeveralToolsByOrderOfAppearance()
    {
        var print = Start(2);
        print.ObserveSlot(Slot(3), SpoolB);
        print.ObserveSlot(Slot(0), SpoolA);

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
        print.ObserveSlot(Slot(1), SpoolA);
        print.MarkCompleted();

        print.BindTools(new Dictionary<int, Guid> { [0] = SpoolB });

        print.Filaments[0].SpoolId.Should().Be(SpoolA);
    }

    [Fact]
    public void Close_LeavesToolsUnbound_WhenTrayCountDoesNotMatch()
    {
        var print = Start(2);
        print.ObserveSlot(Slot(0), SpoolA);

        print.MarkCompleted();

        print.Filaments.Should().OnlyContain(f => f.SpoolId == null);
    }

    [Fact]
    public void ObserveSlot_IgnoresARepeatedTray_AndAnyTrayAfterTheEnd()
    {
        var print = Start(1);
        print.ObserveSlot(Slot(0), SpoolA).Should().BeTrue();
        print.ObserveSlot(Slot(0), SpoolA).Should().BeFalse();

        print.MarkCompleted();

        print.ObserveSlot(Slot(1), SpoolB).Should().BeFalse();
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
    public void ObserveSlot_FillsInASpoolThatWasUnknownWhenTheTrayFirstAppeared()
    {
        var print = Start(1);
        print.ObserveSlot(ExternalPort, null).Should().BeTrue();
        print.WantsSpoolFor(ExternalPort).Should().BeTrue();

        print.ObserveSlot(ExternalPort, SpoolA).Should().BeTrue("the tray was seen without a spool, and now one is known");

        print.WantsSpoolFor(ExternalPort).Should().BeFalse();
        print.ObservedSpoolIds.Should().Equal(SpoolA);
        print.MarkCompleted();
        print.Filaments[0].SpoolId.Should().Be(SpoolA);
    }

    [Fact]
    public void ObserveSlot_NeverReplacesASpoolAlreadyKnownForTheTray()
    {
        var print = Start(1);
        print.ObserveSlot(Slot(1), SpoolA);

        print.ObserveSlot(Slot(1), SpoolB).Should().BeFalse();

        print.ObservedSpoolIds.Should().Equal(SpoolA);
    }

    [Fact]
    public void WantsSpoolFor_IsTrueForANewTray_AndFalseOnceTheRunHasEnded()
    {
        var print = Start(1);
        print.WantsSpoolFor(Slot(3)).Should().BeTrue();

        print.MarkCompleted();

        print.WantsSpoolFor(Slot(3)).Should().BeFalse();
    }
}
