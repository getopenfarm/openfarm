namespace OpenFarm.Domain.Parts;

/// <summary>The headless slicer CLI failed or timed out (research.md §4).</summary>
public sealed class SlicingFailedException(string reason) : Exception(reason);
