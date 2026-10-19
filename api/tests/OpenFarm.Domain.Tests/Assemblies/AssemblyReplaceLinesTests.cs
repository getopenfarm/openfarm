using FluentAssertions;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Domain.Tests.Assemblies;

/// <summary>TDD target — the full-replace line diff behind PUT /assemblies/{id} (keep/update, remove, add).</summary>
public class AssemblyReplaceLinesTests
{
    private static BomLineSpec Part(Guid id, decimal quantity = 1) => new(BomLineTargetKind.Part, id, null, null, quantity);

    private static BomLineSpec SubAssembly(Guid id, decimal quantity = 1) => new(BomLineTargetKind.Assembly, null, id, null, quantity);

    private static BomLineSpec Component(Guid id, decimal quantity = 1) => new(BomLineTargetKind.NonPrintedComponent, null, null, id, quantity);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveComponentQuantity_Throws(decimal quantity)
    {
        var act = () => Assembly.Create("A", null, [Component(Guid.NewGuid(), quantity)]);

        act.Should().Throw<InvalidBomLineQuantityException>();
    }

    [Fact]
    public void ReplaceLines_MeasuredComponentQuantity_IsAllowedBeforeCatalogUnitValidation()
    {
        var target = Guid.NewGuid();
        var assembly = Assembly.Create("A", null, [Component(target, 4)]);

        assembly.ReplaceLines([new BomLineEdit(assembly.Lines[0].Id, Component(target, 4.5m))]);
        assembly.Lines[0].Quantity.Should().Be(4.5m);
    }

    [Fact]
    public void ReplaceLines_UpdatesKeptLines_RemovesMissing_AndReturnsOnlyAdded()
    {
        var keptPart = Guid.NewGuid();
        var assembly = Assembly.Create("A", null, [Part(keptPart), Part(Guid.NewGuid())]);
        var kept = assembly.Lines[0];
        var newSub = Guid.NewGuid();

        var added = assembly.ReplaceLines([new BomLineEdit(kept.Id, Part(keptPart, 4)), new BomLineEdit(null, SubAssembly(newSub, 2))]);

        assembly.Lines.Should().HaveCount(2);
        assembly.Lines.Single(l => l.Id == kept.Id).Quantity.Should().Be(4);
        added.Should().ContainSingle().Which.TargetAssemblyId.Should().Be(newSub);
        assembly.Lines.Should().Contain(added[0]);
    }

    [Fact]
    public void ReplaceLines_CanSwapEveryExistingLineForNewOnes()
    {
        var assembly = Assembly.Create("A", null, [Part(Guid.NewGuid())]);
        var replacement = Guid.NewGuid();

        var added = assembly.ReplaceLines([new BomLineEdit(null, Part(replacement, 3))]);

        assembly.Lines.Should().ContainSingle().Which.TargetPartId.Should().Be(replacement);
        added.Should().ContainSingle();
    }

    [Fact]
    public void ReplaceLines_RemoveAndReAddSameTarget_IsNotADuplicate()
    {
        var target = Guid.NewGuid();
        var assembly = Assembly.Create("A", null, [Part(target)]);

        assembly.Invoking(a => a.ReplaceLines([new BomLineEdit(null, Part(target, 2))])).Should().NotThrow();
    }

    [Fact]
    public void ReplaceLines_Empty_Throws()
    {
        var assembly = Assembly.Create("A", null, [Part(Guid.NewGuid())]);

        assembly.Invoking(a => a.ReplaceLines([])).Should().Throw<AssemblyMustHaveAtLeastOneLineException>();
        assembly.Lines.Should().HaveCount(1);
    }

    [Fact]
    public void ReplaceLines_UnknownLineId_ThrowsWithoutMutating()
    {
        var assembly = Assembly.Create("A", null, [Part(Guid.NewGuid()), Part(Guid.NewGuid())]);

        assembly.Invoking(a => a.ReplaceLines([new BomLineEdit(Guid.NewGuid(), Part(Guid.NewGuid()))]))
            .Should().Throw<BomLineNotFoundException>();
        assembly.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void ReplaceLines_DuplicateTargetAmongNewLines_ThrowsWithoutMutating()
    {
        var original = Guid.NewGuid();
        var assembly = Assembly.Create("A", null, [Part(original)]);
        var dup = Guid.NewGuid();

        assembly.Invoking(a => a.ReplaceLines([new BomLineEdit(null, Part(dup)), new BomLineEdit(null, Part(dup, 2))]))
            .Should().Throw<DuplicateBomLineTargetException>();
        assembly.Lines.Should().ContainSingle().Which.TargetPartId.Should().Be(original);
    }

    [Fact]
    public void ReplaceLines_InvalidQuantityOnKeptLine_ThrowsWithoutMutating()
    {
        var target = Guid.NewGuid();
        var assembly = Assembly.Create("A", null, [Part(target, 2)]);
        var line = assembly.Lines[0];

        assembly.Invoking(a => a.ReplaceLines([new BomLineEdit(line.Id, Part(target, 1.5m))]))
            .Should().Throw<InvalidBomLineQuantityException>();
        line.Quantity.Should().Be(2);
    }

    [Fact]
    public void ReplaceLines_ChangingAKeptLinesTarget_Throws()
    {
        var assembly = Assembly.Create("A", null, [Part(Guid.NewGuid())]);

        assembly.Invoking(a => a.ReplaceLines([new BomLineEdit(assembly.Lines[0].Id, Part(Guid.NewGuid()))]))
            .Should().Throw<ArgumentException>();
    }
}
