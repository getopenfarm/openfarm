using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class ExternalPrintTests
{
    private static readonly Guid SpoolA = Guid.NewGuid();
    private static readonly Guid SpoolB = Guid.NewGuid();

    private static ExternalPrint Start(int tools) => ExternalPrint.Start(Guid.NewGuid(), Guid.NewGuid(), "benchy",
        Enumerable.Range(0, tools).Select(i => new ExternalFilamentSpec(i, "PLA", null, 10, null)));

    [Fact]
    public void Finish_BindsTheFirstTraySeen_ForASingleTool()
    {
        var print = Start(1);
        print.ObserveTray(1, SpoolA);
        print.ObserveTray(2, SpoolB);

        print.Finish(PrintJobStatus.Completed);

        print.Filaments[0].SpoolId.Should().Be(SpoolA);
    }

    [Fact]
    public void Finish_BindsToolsInOrderToTraysInOrderOfAppearance()
    {
        var print = Start(2);
        print.ObserveTray(3, SpoolB);
        print.ObserveTray(0, SpoolA);

        print.Finish(PrintJobStatus.Failed);

        print.Filaments.Select(f => f.SpoolId).Should().Equal(SpoolB, SpoolA);
    }

    [Fact]
    public void Finish_LeavesToolsUnbound_WhenTrayCountDoesNotMatch()
    {
        var print = Start(2);
        print.ObserveTray(0, SpoolA);

        print.Finish(PrintJobStatus.Completed);

        print.Filaments.Should().OnlyContain(f => f.SpoolId == null);
    }

    [Fact]
    public void ObserveTray_IgnoresARepeatedTray_AndAnyTrayAfterTheEnd()
    {
        var print = Start(1);
        print.ObserveTray(0, SpoolA).Should().BeTrue();
        print.ObserveTray(0, SpoolA).Should().BeFalse();

        print.Finish(PrintJobStatus.Completed);

        print.ObserveTray(1, SpoolB).Should().BeFalse();
    }
}
