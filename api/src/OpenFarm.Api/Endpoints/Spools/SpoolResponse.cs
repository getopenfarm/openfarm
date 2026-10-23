using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class SpoolResponse
{
    public Guid Id { get; set; }

    public string? Label { get; set; }

    public Guid FilamentVariantId { get; set; }

    /// <summary>The filament the color belongs to: the spool's page links back to it.</summary>
    public Guid FilamentId { get; set; }

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

    /// <summary>What was paid for the whole spool; null when unknown (zero is a real price).</summary>
    public decimal? Price { get; set; }

    /// <summary><see cref="Price"/> over the spool's full weight in kilograms; null when the price is unknown.</summary>
    public decimal? PricePerKg { get; set; }

    public string? Supplier { get; set; }

    public string? OrderNumber { get; set; }

    /// <summary>The day the spool arrived, as a calendar date.</summary>
    public DateOnly? ReceivedOn { get; set; }

    public static SpoolResponse FromSummary(SpoolSummary summary) => new()
    {
        Id = summary.Id,
        Label = summary.Label,
        FilamentVariantId = summary.FilamentVariantId,
        FilamentId = summary.FilamentId,
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
        Price = summary.Price,
        PricePerKg = summary.PricePerKg,
        Supplier = summary.Supplier,
        OrderNumber = summary.OrderNumber,
        ReceivedOn = summary.ReceivedOn,
    };
}
