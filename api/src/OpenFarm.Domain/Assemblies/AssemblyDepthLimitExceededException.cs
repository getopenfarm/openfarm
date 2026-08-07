namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-006a: a save would exceed the maximum nesting depth.</summary>
public sealed class AssemblyDepthLimitExceededException(int limit)
    : Exception($"Saving this assembly would exceed the maximum nesting depth of {limit} levels.")
{
    public int Limit { get; } = limit;
}
