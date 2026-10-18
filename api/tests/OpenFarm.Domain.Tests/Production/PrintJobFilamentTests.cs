using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class PrintJobFilamentTests
{
    private static PrintJob Job(params JobFilamentRequirementSpec[] lines) => PrintJob.Create(
        "benchy", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null, "hash", 1, lines);

    [Fact]
    public void Create_NumbersFilamentLinesByRequirementOrder_AndIsADispatchedPrint()
    {
        var job = Job(new JobFilamentRequirementSpec("PLA", "000000"), new JobFilamentRequirementSpec("PETG", "ffffff"));

        job.Filaments.Select(f => f.ToolIndex).Should().Equal(0, 1);
        job.Source.Should().Be(PrintSource.Dispatched);
        job.Status.Should().Be(PrintJobStatus.Queued);
        job.PrinterId.Should().BeNull();
    }

    [Fact]
    public void BindFilamentUsage_AssignsByToolOrder()
    {
        var job = Job(new JobFilamentRequirementSpec("PLA", "000000"), new JobFilamentRequirementSpec("PETG", "ffffff"));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        job.BindFilamentUsage([(first, 5m, null), (second, 7m, null)]);

        job.Filaments.Single(f => f.ToolIndex == 0).SpoolId.Should().Be(first);
        job.Filaments.Single(f => f.ToolIndex == 1).EstimatedGrams.Should().Be(7m);
    }

    [Fact]
    public void Dispatch_SetsThePrinterAndRevertClearsIt()
    {
        var job = Job(new JobFilamentRequirementSpec("PLA", null));
        var printer = Guid.NewGuid();

        job.Dispatch(printer);
        job.PrinterId.Should().Be(printer);

        job.RevertDispatch();
        job.PrinterId.Should().BeNull();
    }
}
