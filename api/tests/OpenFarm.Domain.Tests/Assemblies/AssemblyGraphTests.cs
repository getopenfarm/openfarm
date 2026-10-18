using FluentAssertions;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Domain.Tests.Assemblies;

/// <summary>
/// TDD target (research.md R2) — a pure, in-memory cycle/depth validation algorithm over the
/// Assembly-to-Assembly edge list. No EF/DB dependency (constitution Principle II).
/// </summary>
public class AssemblyGraphTests
{
    [Fact]
    public void ValidateSave_DirectSelfReference_ThrowsCycleException()
    {
        var graph = AssemblyGraph.FromEdges([]);
        var handle = Guid.NewGuid();

        var act = () => graph.ValidateSave(handle, handle);

        act.Should().Throw<AssemblyCycleException>()
            .Which.ContainmentPath.Should().Equal(handle, handle);
    }

    [Fact]
    public void ValidateSave_TransitiveCycle_ThrowsCycleExceptionWithOffendingPath()
    {
        var handle = Guid.NewGuid();
        var enclosure = Guid.NewGuid();

        // Enclosure already includes Handle.
        var graph = AssemblyGraph.FromEdges([(enclosure, handle)]);

        // Handle attempting to include Enclosure would close the cycle Handle -> Enclosure -> Handle.
        var act = () => graph.ValidateSave(handle, enclosure);

        act.Should().Throw<AssemblyCycleException>()
            .Which.ContainmentPath.Should().Equal(handle, enclosure, handle);
    }

    [Fact]
    public void ValidateSave_ChainOfExactlyTenLevels_Succeeds()
    {
        // A1 already contains A2 -> A3 -> ... -> A10 (9 existing edges); saving A1 with a fresh line
        // to A2 is the 10th level being added on top of nothing above A1 — but to exercise the "an
        // 11th level is rejected" half of this test meaningfully, build the chain incrementally: each
        // assembly Ai (i = 9 downto 1) gains a line to A(i+1), validated against the edges below it.
        var chain = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray(); // chain[0] = A1 ... chain[9] = A10

        var edges = new List<(Guid, Guid)>();
        for (var i = 8; i >= 0; i--)
        {
            var graph = AssemblyGraph.FromEdges(edges);

            var act = () => graph.ValidateSave(chain[i], chain[i + 1]);
            act.Should().NotThrow($"level {i} of a 10-level chain must be within the limit");

            edges.Add((chain[i], chain[i + 1]));
        }

        // The full 10-level chain (A1..A10) now exists. Confirm it really is all 10 levels by
        // checking that one more level on top is rejected (next assertion).
        edges.Should().HaveCount(9);
    }

    [Fact]
    public void ValidateSave_EleventhLevelOnTopOfTenLevelChain_ThrowsDepthLimitExceededWithLimit()
    {
        var chain = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray(); // A1..A10, full 10 levels
        var edges = new List<(Guid, Guid)>();
        for (var i = 0; i < chain.Length - 1; i++)
        {
            edges.Add((chain[i], chain[i + 1]));
        }

        var graph = AssemblyGraph.FromEdges(edges);
        var a0 = Guid.NewGuid();

        var act = () => graph.ValidateSave(a0, chain[0]);

        act.Should().Throw<AssemblyDepthLimitExceededException>()
            .Which.Limit.Should().Be(10);
    }

    [Fact]
    public void ValidateSave_UnrelatedParentPushesExistingChainOverLimit_ThrowsDepthLimitExceeded()
    {
        // A1..A10 already at the full 10-level limit. A brand-new, previously-unrelated assembly B
        // includes A1 — the edit is to B, not to any assembly already on the A-chain, but it still
        // must be rejected because it pushes A1's own consumers deeper (FR-008's upward evaluation).
        var chain = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var edges = new List<(Guid, Guid)>();
        for (var i = 0; i < chain.Length - 1; i++)
        {
            edges.Add((chain[i], chain[i + 1]));
        }

        var graph = AssemblyGraph.FromEdges(edges);
        var b = Guid.NewGuid();

        var act = () => graph.ValidateSave(b, chain[0]);

        act.Should().Throw<AssemblyDepthLimitExceededException>();
    }

    [Fact]
    public void ValidateSave_ValidAddWithNoCycleOrDepthIssue_DoesNotThrow()
    {
        var enclosure = Guid.NewGuid();
        var handle = Guid.NewGuid();
        var bracket = Guid.NewGuid();

        var graph = AssemblyGraph.FromEdges([(enclosure, handle)]);

        var act = () => graph.ValidateSave(enclosure, bracket);

        act.Should().NotThrow();
    }
}
