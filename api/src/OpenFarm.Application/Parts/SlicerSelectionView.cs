namespace OpenFarm.Application.Parts;

/// <summary>The recorded engine+version a job/part was (or is being) produced with (FR-005, contracts/slicer-management-api.md) — <c>null</c> for a non-slicing file or a legacy pre-feature row.</summary>
public sealed record SlicerSelectionView(string Engine, string Version);
