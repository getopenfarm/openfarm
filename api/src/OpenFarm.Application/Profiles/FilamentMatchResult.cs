using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// Returned by <see cref="RepresentativeFilamentResolver.ResolveAsync"/>, consumed only by
/// <see cref="Parts.ResolveSlicingProfileSelection"/> within the same call — never stored, never
/// serialized to a response (data-model.md).
/// </summary>
public sealed record FilamentMatchResult(FilamentProfile Profile, FilamentMatchSource Source);
