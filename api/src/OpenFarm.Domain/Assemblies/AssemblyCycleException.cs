namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// FR-007: a save would create a direct or transitive containment cycle. <see cref="ContainmentPath"/>
/// is the offending path, starting and ending with the assembly being saved — e.g. for a direct
/// self-reference, <c>[X, X]</c>; for a transitive cycle, <c>[X, Y, ..., X]</c>.
/// </summary>
public sealed class AssemblyCycleException(IReadOnlyList<Guid> containmentPath)
    : Exception($"Saving this assembly would create a cycle: {string.Join(" -> ", containmentPath)}.")
{
    public IReadOnlyList<Guid> ContainmentPath { get; } = containmentPath;
}
