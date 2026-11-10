using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class PrintJobPhasesTests
{
    [Theory]
    [InlineData(PrintJobStatus.Queued, false, PrintJobPhase.Queued)]
    [InlineData(PrintJobStatus.Queued, true, PrintJobPhase.Starting)]
    [InlineData(PrintJobStatus.Printing, true, PrintJobPhase.Printing)]
    [InlineData(PrintJobStatus.Paused, true, PrintJobPhase.Paused)]
    [InlineData(PrintJobStatus.Completed, true, null)]
    [InlineData(PrintJobStatus.Failed, true, null)]
    [InlineData(PrintJobStatus.Cancelled, false, null)]
    public void Of_SeparatesAJustDispatchedJobFromAQueuedOne(PrintJobStatus status, bool dispatched, PrintJobPhase? expected) =>
        PrintJobPhases.Of(status, dispatched ? DateTimeOffset.UtcNow : null).Should().Be(expected);
}
