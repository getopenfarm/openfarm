using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class PrintEndTests
{
    private static readonly DateTimeOffset LastHeard = new(2026, 10, 25, 2, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(99, null, null, true)]
    [InlineData(100, null, null, true)]
    [InlineData(98, null, null, false)]
    [InlineData(null, 120, 120, true)]
    [InlineData(50, 119, 120, false)]
    [InlineData(null, 0, 0, false)]
    [InlineData(null, null, null, false)]
    public void LooksFinished_ByPercentOrLastLayer(int? percent, int? layer, int? totalLayers, bool expected) =>
        PrintEnd.LooksFinished(percent, layer, totalLayers).Should().Be(expected);

    [Fact]
    public void AFinishedPrint_EndedWhenItsRemainingTimeRanOut() =>
        PrintEnd.Infer(PrintJobStatus.Completed, LastHeard, remainingMinutes: 40, nowUtc: LastHeard.AddHours(8))
            .Should().Be(LastHeard.AddMinutes(40));

    [Fact]
    public void AFinishedPrint_NeverEndsAfterNow() =>
        PrintEnd.Infer(PrintJobStatus.Completed, LastHeard, remainingMinutes: 40, nowUtc: LastHeard.AddMinutes(10))
            .Should().Be(LastHeard.AddMinutes(10));

    [Theory]
    [InlineData(PrintJobStatus.Failed, 40)]
    [InlineData(PrintJobStatus.Cancelled, 40)]
    [InlineData(PrintJobStatus.Completed, null)]
    public void AnyOtherEnd_IsWhenThePrinterWasLastHeard(PrintJobStatus outcome, int? remainingMinutes) =>
        PrintEnd.Infer(outcome, LastHeard, remainingMinutes, LastHeard.AddHours(8)).Should().Be(LastHeard);
}
