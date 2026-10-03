using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class SetRunLineBackfillLimitsCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_UnlimitedLine_UpdatesLimits()
    {
        var run = ProductionRun.Create(
            Organization.WellKnownId, null, null, null, null,
            [new RunLineSpec(RunLineTargetKind.Part, Guid.NewGuid(), null, null, AssignmentMode.Unassigned, null, 1, null)]);
        var handler = new SetRunLineBackfillLimitsCommandHandler(new InMemoryProductionRunRepository(run));

        var line = await handler.HandleAsync(
            new SetRunLineBackfillLimitsCommand(run.Lines[0].Id, 3, 500), CancellationToken.None);

        line.BackfillPrinterCap.Should().Be(3);
        line.BackfillCumulativeUnitCeiling.Should().Be(500);
    }

    [Fact]
    public async Task HandleAsync_FiniteLine_ThrowsNotEligible()
    {
        var run = ProductionRun.Create(
            Organization.WellKnownId, null, null, null, null,
            [new RunLineSpec(RunLineTargetKind.Part, Guid.NewGuid(), 5, null, AssignmentMode.Unassigned, null, 1, null)]);
        var handler = new SetRunLineBackfillLimitsCommandHandler(new InMemoryProductionRunRepository(run));

        var act = () => handler.HandleAsync(new SetRunLineBackfillLimitsCommand(run.Lines[0].Id, 3, null), CancellationToken.None);

        await act.Should().ThrowAsync<RunLineNotEligibleForBackfillLimitsException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownLine_ThrowsNotFound()
    {
        var handler = new SetRunLineBackfillLimitsCommandHandler(new InMemoryProductionRunRepository());

        var act = () => handler.HandleAsync(new SetRunLineBackfillLimitsCommand(Guid.NewGuid(), 1, null), CancellationToken.None);

        await act.Should().ThrowAsync<RunLineNotFoundException>();
    }
}

public class SetRunLineRevisionModeCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ExistingLine_UpdatesRevisionMode()
    {
        var run = ProductionRun.Create(
            Organization.WellKnownId, null, null, null, null,
            [new RunLineSpec(RunLineTargetKind.Part, Guid.NewGuid(), 5, RevisionMode.Snapshot, AssignmentMode.Unassigned, null, 1, null)]);
        var handler = new SetRunLineRevisionModeCommandHandler(new InMemoryProductionRunRepository(run));

        var line = await handler.HandleAsync(
            new SetRunLineRevisionModeCommand(run.Lines[0].Id, RevisionMode.FollowLatest), CancellationToken.None);

        line.RevisionMode.Should().Be(RevisionMode.FollowLatest);
    }

    [Fact]
    public async Task HandleAsync_UnknownLine_ThrowsNotFound()
    {
        var handler = new SetRunLineRevisionModeCommandHandler(new InMemoryProductionRunRepository());

        var act = () => handler.HandleAsync(new SetRunLineRevisionModeCommand(Guid.NewGuid(), RevisionMode.Snapshot), CancellationToken.None);

        await act.Should().ThrowAsync<RunLineNotFoundException>();
    }
}
