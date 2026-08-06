using FluentAssertions;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Tests.Assemblies;

/// <summary>
/// TDD target (research.md R6) — floor(min over printed parts of (good quantity / per-unit
/// quantity)), against a fake <see cref="IAssemblyGraphQueryService.ExplodeAsync"/> result. Pure,
/// deterministic calculation; no DB dependency.
/// </summary>
public class CalculateBuildableUnitsQueryHandlerTests
{
    private static readonly Guid AssemblyId = Guid.NewGuid();
    private static readonly Guid PartA = Guid.NewGuid();
    private static readonly Guid PartB = Guid.NewGuid();
    private static readonly Guid Screw = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_WorkedExample_ReturnsFloorOfMinimum()
    {
        // 2 A + 3 B per unit; 10 A + 12 B on hand -> min(floor(10/2)=5, floor(12/3)=4) = 4.
        var explosion = new BomExplosionResult(
            [new ExplodedPartQuantity(PartA, "A", 2), new ExplodedPartQuantity(PartB, "B", 3)],
            []);
        var handler = CreateHandler(explosion);

        var result = await handler.HandleAsync(
            new CalculateBuildableUnitsQuery(AssemblyId, new Dictionary<Guid, decimal> { [PartA] = 10, [PartB] = 12 }),
            CancellationToken.None);

        result.IsApplicable.Should().BeTrue();
        result.Value.Should().Be(4);
    }

    [Fact]
    public async Task HandleAsync_ZeroGoodQuantityForOneComponent_ReturnsZeroEvenWhenOthersAreAbundant()
    {
        var explosion = new BomExplosionResult(
            [new ExplodedPartQuantity(PartA, "A", 2), new ExplodedPartQuantity(PartB, "B", 3)],
            []);
        var handler = CreateHandler(explosion);

        var result = await handler.HandleAsync(
            new CalculateBuildableUnitsQuery(AssemblyId, new Dictionary<Guid, decimal> { [PartA] = 90, [PartB] = 0 }),
            CancellationToken.None);

        result.IsApplicable.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_PartOmittedFromGoodQuantities_TreatedAsZero()
    {
        var explosion = new BomExplosionResult([new ExplodedPartQuantity(PartA, "A", 2)], []);
        var handler = CreateHandler(explosion);

        var result = await handler.HandleAsync(
            new CalculateBuildableUnitsQuery(AssemblyId, new Dictionary<Guid, decimal>()),
            CancellationToken.None);

        result.IsApplicable.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_NonPrintedLinesPresent_NeverAffectTheResult()
    {
        var withoutNonPrinted = new BomExplosionResult([new ExplodedPartQuantity(PartA, "A", 2)], []);
        var withNonPrinted = new BomExplosionResult(
            [new ExplodedPartQuantity(PartA, "A", 2)],
            [new ExplodedNonPrintedComponentQuantity(Screw, "Screw", "each", 1000)]);

        var goodQuantities = new Dictionary<Guid, decimal> { [PartA] = 10 };

        var resultWithout = await CreateHandler(withoutNonPrinted)
            .HandleAsync(new CalculateBuildableUnitsQuery(AssemblyId, goodQuantities), CancellationToken.None);
        var resultWith = await CreateHandler(withNonPrinted)
            .HandleAsync(new CalculateBuildableUnitsQuery(AssemblyId, goodQuantities), CancellationToken.None);

        resultWith.Value.Should().Be(resultWithout.Value);
    }

    [Fact]
    public async Task HandleAsync_NoPrintedPartsAnywhereInTree_ReturnsNotApplicableWithReason()
    {
        var explosion = new BomExplosionResult([], [new ExplodedNonPrintedComponentQuantity(Screw, "Screw", "each", 4)]);
        var handler = CreateHandler(explosion);

        var result = await handler.HandleAsync(
            new CalculateBuildableUnitsQuery(AssemblyId, new Dictionary<Guid, decimal>()),
            CancellationToken.None);

        result.IsApplicable.Should().BeFalse();
        result.Value.Should().BeNull();
        result.NotApplicableReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task HandleAsync_UnknownAssemblyId_ThrowsAssemblyNotFound()
    {
        var handler = CreateHandler(new BomExplosionResult([], []), assemblyExists: false);

        var act = () => handler.HandleAsync(
            new CalculateBuildableUnitsQuery(AssemblyId, new Dictionary<Guid, decimal>()),
            CancellationToken.None);

        await act.Should().ThrowAsync<AssemblyNotFoundException>();
    }

    private static CalculateBuildableUnitsQueryHandler CreateHandler(BomExplosionResult explosion, bool assemblyExists = true) =>
        new(new FakeAssemblyGraphQueryService(explosion), new FakeAssemblyExistenceCheck(assemblyExists));

    private sealed class FakeAssemblyGraphQueryService(BomExplosionResult result) : IAssemblyGraphQueryService
    {
        public Task<BomExplosionResult> ExplodeAsync(Guid organizationId, Guid rootAssemblyId, int quantity, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public Task<IReadOnlyList<(Guid ParentAssemblyId, Guid ChildAssemblyId)>> GetOrganizationEdgesAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(Guid, Guid)>>([]);
    }

    private sealed class FakeAssemblyExistenceCheck(bool exists) : IAssemblyRepository
    {
        public Task AddAsync(Assembly assembly, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Assembly?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Assembly?>(null);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(exists);

        public Task<(IReadOnlyList<Assembly> Assemblies, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyList<Assembly>, int)>(([], 0));

        public Task<IReadOnlyList<string>> GetReferencingAssemblyNamesAsync(BomLineTargetKind targetKind, Guid targetId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RemoveAsync(Assembly assembly, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddLineAsync(BomLine line, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TResult> RunWithStructuralLockAsync<TResult>(
            Guid organizationId, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
