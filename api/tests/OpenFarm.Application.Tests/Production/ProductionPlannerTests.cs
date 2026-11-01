using FluentAssertions;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class ProductionPlannerTests
{
    [Fact]
    public async Task AssemblyPlatePlan_DefaultsToOnePlatePerPart_AndUsesRecipeRatiosForSets()
    {
        var bracket = ProductionTestFixtures.CreatePart(unitsPerPrint: 6);
        var cover = ProductionTestFixtures.CreatePart(unitsPerPrint: 4);
        var planner = new ProductionPlanner(new InMemoryPartRepository(bracket, cover),
            new FakeAssemblyGraphQueryService(new BomExplosionResult([
                new(bracket.Id, bracket.Name, 2), new(cover.Id, cover.Name, 1)], [])));
        var input = new ProductionPlanInput(RunLineTargetKind.Assembly, Guid.NewGuid(), 1, QuantityBasis.Plates);
        var plan = await planner.PlanAsync(input, default);
        plan.Plates.Should().Be(2);
        plan.CompleteSets.Should().Be(3);
        plan.Parts.Should().OnlyContain(p => p.JobUnits!.Count == 1 && p.JobUnits[0] == p.UnitsPerPlate);
        var skipped = await planner.PlanAsync(input with { PlateOverrides = new Dictionary<Guid, int> { [bracket.Id] = 2, [cover.Id] = 0 } }, default);
        skipped.Plates.Should().Be(2);
        skipped.CompleteSets.Should().Be(0);
        skipped.Parts.Single(p => p.PartId == cover.Id).JobUnits.Should().BeEmpty();
        skipped.Parts.Single(p => p.PartId == bracket.Id).RequiredUnits.Should().Be(12);
        skipped.Version.Should().NotBe(plan.Version);
    }

    [Fact]
    public async Task PlatePlanVersion_ChangesWhenRecipeRatiosChangeEvenWithSameOutput()
    {
        var part = ProductionTestFixtures.CreatePart(unitsPerPrint: 6);
        var parts = new InMemoryPartRepository(part);
        var first = new ProductionPlanner(parts, new FakeAssemblyGraphQueryService(new BomExplosionResult([new(part.Id, part.Name, 2)], [])));
        var second = new ProductionPlanner(parts, new FakeAssemblyGraphQueryService(new BomExplosionResult([new(part.Id, part.Name, 3)], [])));
        var input = new ProductionPlanInput(RunLineTargetKind.Assembly, Guid.NewGuid(), 1, QuantityBasis.Plates);
        var before = await first.PlanAsync(input, default);
        var after = await second.PlanAsync(input, default);
        before.Parts.Should().BeEquivalentTo(after.Parts);
        before.Version.Should().NotBe(after.Version);
        before.CompleteSets.Should().Be(3);
        after.CompleteSets.Should().Be(2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task InvalidComponentCounts_AreRejected(int count)
    {
        var part = ProductionTestFixtures.CreatePart(unitsPerPrint: 6);
        var planner = new ProductionPlanner(new InMemoryPartRepository(part),
            new FakeAssemblyGraphQueryService(new BomExplosionResult([new(part.Id, part.Name, 2)], [])));
        var input = new ProductionPlanInput(RunLineTargetKind.Assembly, Guid.NewGuid(), 1, QuantityBasis.Plates,
            new Dictionary<Guid, int> { [part.Id] = count });
        var act = () => planner.PlanAsync(input, default);
        await act.Should().ThrowAsync<InvalidProductionDemandException>();
    }

    [Fact]
    public async Task OverridesForUnknownPartsOrSetTargets_AreRejected()
    {
        var part = ProductionTestFixtures.CreatePart();
        var planner = new ProductionPlanner(new InMemoryPartRepository(part),
            new FakeAssemblyGraphQueryService(new BomExplosionResult([new(part.Id, part.Name, 2)], [])));
        var input = new ProductionPlanInput(RunLineTargetKind.Assembly, Guid.NewGuid(), 1, QuantityBasis.Plates,
            new Dictionary<Guid, int> { [Guid.NewGuid()] = 1 });
        await FluentActions.Invoking(() => planner.PlanAsync(input, default)).Should().ThrowAsync<InvalidProductionDemandException>();
        await FluentActions.Invoking(() => planner.PlanAsync(input with { QuantityBasis = QuantityBasis.Sets }, default)).Should().ThrowAsync<InvalidProductionDemandException>();
    }

    [Fact]
    public void AssemblyPlateDemand_CannotBelongToRunOrBackfill()
    {
        var spec = new ProductionDemandSpec(RunLineTargetKind.Assembly, Guid.NewGuid(), 1, null, AssignmentMode.Unassigned, null, 1, null, QuantityBasis.Plates);
        ProductionDemand.Create(null, spec).ProductionRunId.Should().BeNull();
        FluentActions.Invoking(() => ProductionDemand.Create(Guid.NewGuid(), spec)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ProductionDemand.Create(null, spec with { Quantity = null })).Should().Throw<ArgumentException>();
    }
}
