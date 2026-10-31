using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <param name="Quantity">Target units; null on an unlimited line.</param>
/// <param name="CompletedUnits">Units actually produced; historical whole-plate jobs may exceed the target.</param>
/// <param name="EstimatedMinutesPerPlate">From the plate's snapshot, else the library part; null when unknown.</param>
/// <param name="BlockingReason">Why the next waiting plate cannot go to any printer now; null when one can take it.</param>
/// <param name="Jobs">Every plate, in queue order; a reprint sits next to the plate it replaces.</param>
public sealed record PartRequirementSummary(
    Guid Id,
    Guid PartId,
    string PartName,
    decimal? Quantity,
    bool SourcePartAvailable,
    int UnitsPerPlate,
    int CompletedUnits,
    int WrittenOffUnits,
    PlateCounts Plates,
    int? EstimatedMinutesPerPlate,
    decimal? EstimatedGramsPerPlate,
    string? BlockingReason,
    IReadOnlyList<RunJobSummary> Jobs);

/// <param name="CanRequeue">A lost plate of a finite line on an open run, with the snapshot a reprint needs.</param>
public sealed record RunJobSummary(
    Guid Id,
    PrintJobStatus Status,
    Guid? PrinterId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? LastBlockReason,
    Guid? RequeuedAsJobId,
    bool WrittenOff,
    bool NeedsAction,
    bool CanRequeue, int? TargetUnits = null, int? ProducedUnits = null, int? MissingUnits = null, bool OutputVerified = true);
