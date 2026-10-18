using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class SetRunLineRevisionModeCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ExistingFiniteLine_RejectsFollowLatest()
    {
        var run = ProductionRun.Create(
            null, null, null, null,
            [new RunLineSpec(RunLineTargetKind.Part, Guid.NewGuid(), 5, RevisionMode.Snapshot, AssignmentMode.Unassigned, null, 1, null)]);
        var handler = new SetRunLineRevisionModeCommandHandler(new InMemoryProductionRunRepository(run));

        var act = () => handler.HandleAsync(
            new SetRunLineRevisionModeCommand(run.Lines[0].Id, RevisionMode.FollowLatest), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRevisionModeException>();
        run.Lines[0].RevisionMode.Should().Be(RevisionMode.Snapshot);
    }

    [Fact]
    public async Task HandleAsync_UnknownLine_ThrowsNotFound()
    {
        var handler = new SetRunLineRevisionModeCommandHandler(new InMemoryProductionRunRepository());

        var act = () => handler.HandleAsync(new SetRunLineRevisionModeCommand(Guid.NewGuid(), RevisionMode.Snapshot), CancellationToken.None);

        await act.Should().ThrowAsync<RunLineNotFoundException>();
    }
}
