using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record GetPrintJobQuery(Guid JobId);

/// <param name="PricePerKg">The price the line was costed at when first deducted; null while no price was known.</param>
/// <param name="Cost">The grams deducted at that price, unrounded; null without a price or without grams.</param>
public sealed record JobFilamentConsumptionDetail(Guid Id, string? MaterialType, string? DetectedColorHex, Guid? SpoolId, decimal? GramsConsumed, Guid? NarrowFilamentVariantId = null, Guid? NarrowFilamentProfileId = null,
    int ToolIndex = 0, decimal? EstimatedGrams = null, decimal? PricePerKg = null, FilamentPriceSource? PriceSource = null, decimal? Cost = null);

public sealed record PrintJobDetail(
    Guid Id,
    Guid? PartRequirementId,
    double? QueuePosition,
    int? PlateQuantity,
    AssignmentMode? AssignmentMode,
    Guid? PinnedPrinterId,
    Guid? DispatchedPrinterId,
    PrintJobStatus Status,
    string? FileRevisionHash,
    int? FileRevisionNumber,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int? ActualDurationMinutes,
    DateTimeOffset? CancelRequestedAtUtc,
    Guid? RequeuedAsJobId,
    IReadOnlyList<JobFilamentConsumptionDetail> FilamentConsumption,
    DateTimeOffset CreatedAtUtc, string SnapshotState = "Ready", string? SnapshotRecoveryReason = null,
    PrintSource Source = PrintSource.Dispatched, string Name = "",
    string? LastBlockReason = null, DateTimeOffset? LastBlockedAtUtc = null, DispatchProvenance? Provenance = null);
