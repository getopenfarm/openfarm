namespace OpenFarm.Application.FilamentTracking;

public sealed record GetFeedUnitsQuery(Guid PrinterId, bool IncludeHidden = false, bool IncludeArchived = false);

public sealed record CurrentSpoolSummary(Guid Id, Guid FilamentVariantId, decimal RemainingWeightGrams, decimal SpoolWeightGrams);

public sealed record FeedSlotDetail(
    Guid Id,
    int SlotPosition,
    string DisplayLabel,
    DateTimeOffset? DeactivatedAtUtc,
    CurrentSpoolSummary? CurrentSpool,
    string? DetectedMaterialType,
    string? DetectedColorHex,
    string? DetectedSubBrand);

public sealed record FeedUnitDetail(
    Guid Id,
    string Kind,
    int UnitIndex,
    string DisplayLabel,
    bool IsRevealed,
    Guid? ReplacesFeedUnitId,
    Guid? SupersededByFeedUnitId,
    IReadOnlyList<FeedSlotDetail> FeedSlots,
    DateTimeOffset? ArchivedAtUtc = null,
    Guid? FeedingSlotId = null,
    string? FeedingSelectionSource = null);
