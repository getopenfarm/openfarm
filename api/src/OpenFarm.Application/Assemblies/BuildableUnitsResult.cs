namespace OpenFarm.Application.Assemblies;

/// <summary>
/// FR-010's output — a discriminated shape rather than a bare <c>int</c> so "not applicable" (no
/// printed components anywhere in the tree) can never be silently read as <c>0</c> or as unbounded
/// (research.md R6).
/// </summary>
public sealed record BuildableUnitsResult
{
    public required bool IsApplicable { get; init; }

    /// <summary>Set iff <see cref="IsApplicable"/>.</summary>
    public int? Value { get; init; }

    /// <summary>Set iff <c>!</c><see cref="IsApplicable"/> (FR-010d).</summary>
    public string? NotApplicableReason { get; init; }
}
