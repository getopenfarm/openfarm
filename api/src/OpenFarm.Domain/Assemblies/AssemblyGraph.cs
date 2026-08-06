namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// Pure, in-memory cycle/depth validation over an organization's flat Assembly-to-Assembly edge list
/// (research.md R2) — no EF/DB dependency, exhaustively unit-testable. Deliberately not a second
/// recursive CTE alongside <c>AssemblyGraphQueryService.ExplodeAsync</c>'s (research.md R1): this
/// never touches a Part/NonPrintedComponent row, only the much smaller edge subgraph.
/// </summary>
public sealed class AssemblyGraph
{
    private const int MaxDepth = 10;

    private readonly Dictionary<Guid, List<Guid>> _childrenOf = [];
    private readonly Dictionary<Guid, List<Guid>> _parentsOf = [];

    private AssemblyGraph()
    {
    }

    public static AssemblyGraph FromEdges(IReadOnlyCollection<(Guid ParentAssemblyId, Guid ChildAssemblyId)> edges)
    {
        var graph = new AssemblyGraph();

        foreach (var (parent, child) in edges)
        {
            AddAdjacency(graph._childrenOf, parent, child);
            AddAdjacency(graph._parentsOf, child, parent);
        }

        return graph;
    }

    /// <summary>
    /// Validates that assembly <paramref name="savingAssemblyId"/> (X) gaining/changing a line that
    /// targets sub-assembly <paramref name="newOrChangedTargetAssemblyId"/> (Y) would not create a
    /// direct/transitive cycle (FR-007) or exceed the max nesting depth (FR-006a/FR-008). No-op if
    /// valid; throws <see cref="AssemblyCycleException"/> or <see cref="AssemblyDepthLimitExceededException"/> otherwise.
    /// </summary>
    public void ValidateSave(Guid savingAssemblyId, Guid newOrChangedTargetAssemblyId)
    {
        var cyclePath = FindCyclePath(savingAssemblyId, newOrChangedTargetAssemblyId);
        if (cyclePath is not null)
        {
            throw new AssemblyCycleException(cyclePath);
        }

        var rootDistance = ComputeLongestChain(savingAssemblyId, _parentsOf);
        var subtreeDepth = 1 + ComputeLongestChain(newOrChangedTargetAssemblyId, _childrenOf);
        var totalDepth = rootDistance + subtreeDepth + 1;

        if (totalDepth > MaxDepth)
        {
            throw new AssemblyDepthLimitExceededException(MaxDepth);
        }
    }

    /// <summary>
    /// If X (<paramref name="savingAssemblyId"/>) is reachable downward from Y
    /// (<paramref name="targetAssemblyId"/>) in the existing edge set — meaning adding X -> Y would
    /// close a cycle — returns the full offending path <c>[X, Y, ..., X]</c>; otherwise <c>null</c>.
    /// A direct self-reference (X == Y) is the trivial zero-length case, returning <c>[X, X]</c>.
    /// </summary>
    private List<Guid>? FindCyclePath(Guid savingAssemblyId, Guid targetAssemblyId)
    {
        var downwardPath = new List<Guid> { targetAssemblyId };

        if (!TryFindPathDownward(targetAssemblyId, savingAssemblyId, [], downwardPath))
        {
            return null;
        }

        var fullPath = new List<Guid>(downwardPath.Count + 1) { savingAssemblyId };
        fullPath.AddRange(downwardPath);
        return fullPath;
    }

    private bool TryFindPathDownward(Guid current, Guid target, HashSet<Guid> visited, List<Guid> path)
    {
        if (current == target)
        {
            return true;
        }

        if (!visited.Add(current))
        {
            return false;
        }

        if (_childrenOf.TryGetValue(current, out var children))
        {
            foreach (var child in children)
            {
                path.Add(child);

                if (TryFindPathDownward(child, target, visited, path))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        return false;
    }

    /// <summary>Longest chain of edges starting at <paramref name="node"/> through <paramref name="adjacency"/>, not counting <paramref name="node"/> itself. The existing graph is assumed acyclic (this validation is what maintains that invariant).</summary>
    private static int ComputeLongestChain(Guid node, Dictionary<Guid, List<Guid>> adjacency) =>
        ComputeLongestChain(node, adjacency, []);

    private static int ComputeLongestChain(Guid node, Dictionary<Guid, List<Guid>> adjacency, HashSet<Guid> onStack)
    {
        if (!adjacency.TryGetValue(node, out var neighbors) || neighbors.Count == 0)
        {
            return 0;
        }

        if (!onStack.Add(node))
        {
            return 0;
        }

        var max = 0;
        foreach (var neighbor in neighbors)
        {
            max = Math.Max(max, 1 + ComputeLongestChain(neighbor, adjacency, onStack));
        }

        onStack.Remove(node);
        return max;
    }

    private static void AddAdjacency(Dictionary<Guid, List<Guid>> adjacency, Guid from, Guid to)
    {
        if (!adjacency.TryGetValue(from, out var list))
        {
            list = [];
            adjacency[from] = list;
        }

        list.Add(to);
    }
}
