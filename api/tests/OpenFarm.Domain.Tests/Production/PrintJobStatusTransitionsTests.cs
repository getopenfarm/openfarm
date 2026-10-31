using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class PrintJobStatusTransitionsTests
{
    private static readonly HashSet<(PrintJobStatus From, PrintJobStatus To)> LegalTransitions =
    [
        (PrintJobStatus.Queued, PrintJobStatus.Printing),
        (PrintJobStatus.Printing, PrintJobStatus.Paused),
        (PrintJobStatus.Paused, PrintJobStatus.Printing),
        (PrintJobStatus.Queued, PrintJobStatus.Failed),
        (PrintJobStatus.Printing, PrintJobStatus.Failed),
        (PrintJobStatus.Paused, PrintJobStatus.Failed),
        (PrintJobStatus.Printing, PrintJobStatus.Completed),
        (PrintJobStatus.Paused, PrintJobStatus.Completed),
        (PrintJobStatus.Queued, PrintJobStatus.Cancelled),
        (PrintJobStatus.Printing, PrintJobStatus.Cancelled),
        (PrintJobStatus.Paused, PrintJobStatus.Cancelled),
    ];

    public static IEnumerable<object[]> LegalTransitionData() =>
        LegalTransitions.Select(t => new object[] { t.From, t.To });

    public static IEnumerable<object[]> IllegalTransitionData()
    {
        var allStatuses = Enum.GetValues<PrintJobStatus>();
        foreach (var from in allStatuses)
        {
            foreach (var to in allStatuses)
            {
                if (!LegalTransitions.Contains((from, to)))
                {
                    yield return [from, to];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(LegalTransitionData))]
    public void EnsureValidTransition_LegalEdge_DoesNotThrow(PrintJobStatus from, PrintJobStatus to)
    {
        var act = () => PrintJobStatusTransitions.EnsureValidTransition(from, to);

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(IllegalTransitionData))]
    public void EnsureValidTransition_IllegalEdge_Throws(PrintJobStatus from, PrintJobStatus to)
    {
        var act = () => PrintJobStatusTransitions.EnsureValidTransition(from, to);

        act.Should().Throw<InvalidPrintJobStatusTransitionException>();
    }
}
