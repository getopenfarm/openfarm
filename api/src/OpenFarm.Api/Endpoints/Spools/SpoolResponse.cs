using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class SpoolResponse
{
    public Guid Id { get; set; }

    public string? Label { get; set; }

    public Guid FilamentVariantId { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public string Material { get; set; } = string.Empty;

    public string VendorName { get; set; } = string.Empty;

    public decimal SpoolWeightGrams { get; set; }

    public decimal RemainingWeightGrams { get; set; }

    public string State { get; set; } = string.Empty;

    public Guid? FeedSlotId { get; set; }

    public string? FeedSlotLabel { get; set; }

    public bool HasUsageOverrun { get; set; }

    public static SpoolResponse FromSummary(SpoolSummary summary) => new()
    {
        Id = summary.Id,
        Label = summary.Label,
        FilamentVariantId = summary.FilamentVariantId,
        ColorName = summary.ColorName,
        ColorHex = summary.ColorHex,
        Material = summary.Material,
        VendorName = summary.VendorName,
        SpoolWeightGrams = summary.SpoolWeightGrams,
        RemainingWeightGrams = summary.RemainingWeightGrams,
        State = summary.State.ToString(),
        FeedSlotId = summary.FeedSlotId,
        FeedSlotLabel = summary.FeedSlotLabel,
        HasUsageOverrun = summary.HasUsageOverrun,
    };
}
