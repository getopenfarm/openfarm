using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record GetSlicingDefaultsQuery;

public sealed record EffectiveSlicerDefaultSummary(SlicerEngine Engine, string Version, bool IsExplicit);

public sealed record AvailableSlicerSummary(SlicerEngine Engine, string Version, SlicerPlatform Platform);

/// <summary>
/// The effective farm default (<c>null</c> only when <see cref="Available"/> is empty, FR-011) plus
/// every host-platform-usable bundled slicer (contracts/slicer-management-api.md GET/PUT) — shared
/// by both the read query and the "set default" command's response.
/// </summary>
public sealed record SlicingDefaults(EffectiveSlicerDefaultSummary? Default, IReadOnlyList<AvailableSlicerSummary> Available, EstimationProfileSelection? EstimationProfiles = null);
