namespace OpenFarm.Domain.Parts;

/// <summary>
/// Gates <see cref="Part.EstimatedPrintTimeMinutes"/>/<see cref="Part.EstimatedFilamentWeightGrams"/>
/// (data-model.md). <c>Available</c> immediately for a file that doesn't require slicing. For a
/// requires-slicing file confirmed before its trailing slice finishes (FR-009c/FR-009d), starts
/// <c>Resolving</c> and later flips to <c>Available</c> or, on a deferred slicing failure,
/// permanently <c>Unavailable</c> — the one legitimate way a <see cref="Part"/> can carry incomplete
/// derived data (research.md §3b).
/// </summary>
public enum EstimateStatus
{
    Resolving,
    Available,
    Unavailable,
}
