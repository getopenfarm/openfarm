using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.FeedUnits;

public sealed class CurrentSpoolResponse
{
    public Guid Id { get; set; }

    public Guid FilamentVariantId { get; set; }

    public decimal RemainingWeightGrams { get; set; }

    public static CurrentSpoolResponse FromSummary(CurrentSpoolSummary summary) => new()
    {
        Id = summary.Id,
        FilamentVariantId = summary.FilamentVariantId,
        RemainingWeightGrams = summary.RemainingWeightGrams,
    };
}

public sealed class FeedSlotResponse
{
    public Guid Id { get; set; }

    public int SlotPosition { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public DateTimeOffset? DeactivatedAtUtc { get; set; }

    public CurrentSpoolResponse? CurrentSpool { get; set; }

    public string? DetectedMaterialType { get; set; }

    public string? DetectedColorHex { get; set; }

    public string? DetectedSubBrand { get; set; }

    public static FeedSlotResponse FromDetail(FeedSlotDetail detail) => new()
    {
        Id = detail.Id,
        SlotPosition = detail.SlotPosition,
        DisplayLabel = detail.DisplayLabel,
        DeactivatedAtUtc = detail.DeactivatedAtUtc,
        CurrentSpool = detail.CurrentSpool is { } spool ? CurrentSpoolResponse.FromSummary(spool) : null,
        DetectedMaterialType = detail.DetectedMaterialType,
        DetectedColorHex = detail.DetectedColorHex,
        DetectedSubBrand = detail.DetectedSubBrand,
    };
}

public sealed class FeedUnitResponse
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public int UnitIndex { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsRevealed { get; set; }

    public Guid? ReplacesFeedUnitId { get; set; }

    public Guid? SupersededByFeedUnitId { get; set; }

    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public Guid? FeedingSlotId { get; set; }
    public string? FeedingSelectionSource { get; set; }

    public IReadOnlyList<FeedSlotResponse> FeedSlots { get; set; } = [];

    public static FeedUnitResponse FromDetail(FeedUnitDetail detail) => new()
    {
        Id = detail.Id,
        Kind = detail.Kind,
        UnitIndex = detail.UnitIndex,
        DisplayLabel = detail.DisplayLabel,
        IsRevealed = detail.IsRevealed,
        ReplacesFeedUnitId = detail.ReplacesFeedUnitId,
        SupersededByFeedUnitId = detail.SupersededByFeedUnitId,
        ArchivedAtUtc = detail.ArchivedAtUtc,
        FeedingSlotId = detail.FeedingSlotId,
        FeedingSelectionSource = detail.FeedingSelectionSource,
        FeedSlots = detail.FeedSlots.Select(FeedSlotResponse.FromDetail).ToList(),
    };
}
