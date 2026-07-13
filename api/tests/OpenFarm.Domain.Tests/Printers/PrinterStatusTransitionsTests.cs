using FluentAssertions;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public class PrinterStatusTransitionsTests
{
    [Fact]
    public void EnsureValidManualTransition_AwaitingClearToIdle_DoesNotThrow()
    {
        var act = () => PrinterStatusTransitions.EnsureValidManualTransition(PrinterStatus.AwaitingClear, PrinterStatus.Idle);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(PrinterStatus.Idle, PrinterStatus.AwaitingClear)]
    [InlineData(PrinterStatus.Idle, PrinterStatus.Printing)]
    [InlineData(PrinterStatus.Printing, PrinterStatus.Idle)]
    [InlineData(PrinterStatus.Printing, PrinterStatus.AwaitingClear)]
    [InlineData(PrinterStatus.AwaitingClear, PrinterStatus.Printing)]
    [InlineData(PrinterStatus.AwaitingClear, PrinterStatus.AwaitingClear)]
    [InlineData(PrinterStatus.Idle, PrinterStatus.Idle)]
    public void EnsureValidManualTransition_AnyOtherTransition_Throws(PrinterStatus from, PrinterStatus to)
    {
        var act = () => PrinterStatusTransitions.EnsureValidManualTransition(from, to);

        act.Should().Throw<InvalidPrinterStatusTransitionException>();
    }

    [Theory]
    [InlineData("IDLE", PrinterStatus.Idle)]
    [InlineData("idle", PrinterStatus.Idle)]
    [InlineData("INIT", PrinterStatus.Printing)]
    [InlineData("PREPARE", PrinterStatus.Printing)]
    [InlineData("SLICING", PrinterStatus.Printing)]
    [InlineData("RUNNING", PrinterStatus.Printing)]
    [InlineData("PAUSE", PrinterStatus.Printing)]
    [InlineData("FINISH", PrinterStatus.AwaitingClear)]
    [InlineData("FAILED", PrinterStatus.AwaitingClear)]
    public void ObserveStatus_WithMappedGcodeState_ReturnsExpectedStatus(string gcodeState, PrinterStatus expected)
    {
        var act = () => PrinterStatusTransitions.ObserveStatus(gcodeState);

        act.Should().NotThrow().Which.Should().Be(expected);
    }

    [Theory]
    [InlineData("OFFLINE")]
    [InlineData("UNKNOWN")]
    [InlineData("something-unrecognized")]
    [InlineData("")]
    public void ObserveStatus_WithUnmappedGcodeState_ReturnsNullWithoutThrowing(string gcodeState)
    {
        var act = () => PrinterStatusTransitions.ObserveStatus(gcodeState);

        act.Should().NotThrow().Which.Should().BeNull();
    }
}
