using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// File only — no <c>filamentId</c>, <c>name</c>, or <c>tags</c> (research.md §2/§13, FR-001/FR-002/
/// FR-009b). <see cref="SlicerEngineOverride"/> is the one exception: since processing starts
/// immediately at submit, the engine that will actually run this job's slice (if any) must be known
/// now, not deferred to Confirm — it defaults to the organization's <c>DefaultSlicerEngine</c> when
/// omitted (FR-008).
/// </summary>
public sealed record AddPartCommand(Stream File, string FileName, SlicerEngine? SlicerEngineOverride, Guid? RequestedByUserId);

public sealed record AddPartResult(Guid JobId, PartProcessingJobStatus Status);
