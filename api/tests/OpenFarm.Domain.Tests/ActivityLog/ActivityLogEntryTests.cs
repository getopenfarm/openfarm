using FluentAssertions;
using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Domain.Tests.ActivityLog;

public class ActivityLogEntryTests
{
    private static readonly ActivityChannel Token = new(ActivityChannel.Token, Guid.NewGuid(), "CI script");

    [Fact]
    public void AttributeTo_KeepsExistingDetails_AndFirstChannelWins()
    {
        var entry = ActivityLogEntry.Create("PrintJob", "1", "PrintJobPaused", Guid.NewGuid(), ActorType.Operator,
            new ActivityLogDetails(ReportedState: "PAUSE"));

        entry.AttributeTo(Token);
        entry.AttributeTo(new ActivityChannel(ActivityChannel.App, Guid.NewGuid(), "Claude"));

        entry.Details!.ReportedState.Should().Be("PAUSE");
        entry.Details.Via.Should().Be(Token);
    }

    [Fact]
    public void AttributeTo_LeavesSystemEntriesUnattributed()
    {
        var entry = ActivityLogEntry.Create("Printer", "1", "PrinterStatusChanged", null, ActorType.System);

        entry.AttributeTo(Token);

        entry.Details.Should().BeNull();
    }
}
