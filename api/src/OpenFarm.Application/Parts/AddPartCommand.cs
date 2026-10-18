using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// File only — no <c>filamentId</c>, <c>name</c>, or <c>tags</c> (research.md §2/§13, FR-001/FR-002/
/// FR-009b). <see cref="SlicerEngineOverride"/>/<see cref="SlicerVersionOverride"/> are the one
/// exception: since processing starts immediately at submit, the (engine, version) that will
/// actually run this job's slice (if any) must be known now, not deferred to Confirm — resolved via
/// <c>ResolveSlicerSelection</c> against the effective default when omitted (FR-003/
/// FR-004).
/// </summary>
public sealed record AddPartCommand(Stream File, string FileName, SlicerEngine? SlicerEngineOverride, string? SlicerVersionOverride, Guid? RequestedByUserId);

public sealed record AddPartResult(Guid JobId, PartProcessingJobStatus Status);
