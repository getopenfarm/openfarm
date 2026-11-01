using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.Notifications;

public sealed class NotificationLineResponse
{
    public Guid LineId { get; set; }

    public int ToolIndex { get; set; }

    public string? MaterialType { get; set; }

    public string? ColorHex { get; set; }

    public decimal? EstimatedGrams { get; set; }

    public Guid? SpoolId { get; set; }

    /// <summary>NoSpool, SpoolNotLoaded, NoEstimate or NoProgress; only the first two can be fixed by assigning a spool.</summary>
    public string Reason { get; set; } = string.Empty;
}

public sealed class NotificationItemResponse
{
    /// <summary>"FilamentNotDeducted" today.</summary>
    public string Type { get; set; } = string.Empty;

    public Guid PrintId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>"Dispatched" or "External".</summary>
    public string Source { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public Guid? PrinterId { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    public List<NotificationLineResponse> Lines { get; set; } = [];
}

public sealed class LowStockNotificationResponse
{
    public Guid FilamentId { get; set; }

    public Guid FilamentVariantId { get; set; }

    public string VendorName { get; set; } = string.Empty;

    public string Material { get; set; } = string.Empty;

    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public decimal OnHandGrams { get; set; }

    /// <summary>The grams threshold; null when only the days threshold applies.</summary>
    public decimal? ThresholdGrams { get; set; }

    /// <summary>Days the stock lasts at the last 30 days' pace; null when the colour was not used.</summary>
    public decimal? DaysLeft { get; set; }

    public int? ThresholdDays { get; set; }
}

public sealed class UnregisteredRfidSpoolResponse
{
    public Guid PrinterId { get; set; }

    public string PrinterName { get; set; } = string.Empty;

    public Guid FeedSlotId { get; set; }

    /// <summary>e.g. "AMS 1 · Slot 2".</summary>
    public string SlotLabel { get; set; } = string.Empty;

    public string RfidId { get; set; } = string.Empty;

    public string? MaterialType { get; set; }

    public string? SubBrand { get; set; }

    public string? ColorHex { get; set; }

    /// <summary>The AMS's remaining-percent reading; null until it has measured the spool.</summary>
    public int? RemainPercent { get; set; }

    /// <summary>The spool's full filament weight as its tag states it.</summary>
    public int? RfidWeightGrams { get; set; }

    /// <summary>The grams the reading implies, to prefill a registration.</summary>
    public decimal? RemainingGrams { get; set; }

    /// <summary>The catalog colour it most likely is; null when none, or several, fit.</summary>
    public Guid? FilamentVariantId { get; set; }

    /// <summary>e.g. "PLA Matte Charcoal".</summary>
    public string? VariantName { get; set; }

    /// <summary>An untagged spool loaded in this slot, which Link can tie the tag to.</summary>
    public Guid? LoadedSpoolId { get; set; }

    public string? LoadedSpoolName { get; set; }
}

public sealed class RfidWeightDriftResponse
{
    public Guid PrinterId { get; set; }

    public string PrinterName { get; set; } = string.Empty;

    public Guid FeedSlotId { get; set; }

    public string SlotLabel { get; set; } = string.Empty;

    public Guid SpoolId { get; set; }

    public string? SpoolLabel { get; set; }

    /// <summary>The spool's recorded remaining weight.</summary>
    public decimal InventoryGrams { get; set; }

    /// <summary>What the AMS reading implies, to whole grams.</summary>
    public decimal ReadGrams { get; set; }

    /// <summary>The spool's filament and colour, to name a spool with no label.</summary>
    public string? Material { get; set; }

    public string? ColorName { get; set; }
}

public sealed class ListNotificationsResponse
{
    public List<NotificationItemResponse> Items { get; set; } = [];

    /// <summary>Print items across all pages; <see cref="LowStock"/> is listed whole and not counted here.</summary>
    public int TotalCount { get; set; }

    /// <summary>Colours whose grams on hand dropped under their threshold; each clears once restocked.</summary>
    public List<LowStockNotificationResponse> LowStock { get; set; } = [];

    /// <summary>Tagged spools printers read that no active spool owns; each clears once registered or linked.</summary>
    public List<UnregisteredRfidSpoolResponse> RfidSpools { get; set; } = [];

    /// <summary>Loaded tagged spools the AMS reads well off their recorded weight; each clears once corrected or back in line.</summary>
    public List<RfidWeightDriftResponse> RfidDrift { get; set; } = [];
}

/// <summary>GET /api/v1/notifications — action-required items, newest first; the 50 newest, with the total count.</summary>
[Permission("print-jobs:view", "spools:list", "filaments:list")]
public sealed class ListNotificationsEndpoint(IQueryHandler<ListNotificationsQuery, NotificationsPage> handler)
    : EndpointWithoutRequest<ListNotificationsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/notifications");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var page = await handler.HandleAsync(new ListNotificationsQuery(), ct);
        Response = new ListNotificationsResponse
        {
            TotalCount = page.TotalCount,
            Items = page.Items.Select(item => new NotificationItemResponse
            {
                Type = item.Type,
                PrintId = item.PrintId,
                Name = item.Name,
                Source = item.Source.ToString(),
                Status = item.Status.ToString(),
                PrinterId = item.PrinterId,
                EndedAtUtc = item.EndedAtUtc,
                Lines = item.Lines.Select(line => new NotificationLineResponse
                {
                    LineId = line.LineId,
                    ToolIndex = line.ToolIndex,
                    MaterialType = line.MaterialType,
                    ColorHex = line.ColorHex,
                    EstimatedGrams = line.EstimatedGrams,
                    SpoolId = line.SpoolId,
                    Reason = line.Reason.ToString(),
                }).ToList(),
            }).ToList(),
            LowStock = page.LowStock.Select(item => new LowStockNotificationResponse
            {
                FilamentId = item.FilamentId,
                FilamentVariantId = item.FilamentVariantId,
                VendorName = item.VendorName,
                Material = item.Material,
                ColorName = item.ColorName,
                ColorHex = item.ColorHex,
                OnHandGrams = item.OnHandGrams,
                ThresholdGrams = item.ThresholdGrams,
                DaysLeft = item.DaysLeft,
                ThresholdDays = item.ThresholdDays,
            }).ToList(),
            RfidSpools = page.RfidSpools.Select(item => new UnregisteredRfidSpoolResponse
            {
                PrinterId = item.PrinterId,
                PrinterName = item.PrinterName,
                FeedSlotId = item.FeedSlotId,
                SlotLabel = item.SlotLabel,
                RfidId = item.RfidId,
                MaterialType = item.MaterialType,
                SubBrand = item.SubBrand,
                ColorHex = item.ColorHex,
                RemainPercent = item.RemainPercent,
                RfidWeightGrams = item.RfidWeightGrams,
                RemainingGrams = item.RemainingGrams,
                FilamentVariantId = item.Match?.FilamentVariantId,
                VariantName = item.Match is { } match ? $"{match.Material} {match.ColorName}" : null,
                LoadedSpoolId = item.LoadedSpoolId,
                LoadedSpoolName = item.LoadedSpoolName,
            }).ToList(),
            RfidDrift = page.RfidDrift.Select(item => new RfidWeightDriftResponse
            {
                PrinterId = item.PrinterId,
                PrinterName = item.PrinterName,
                FeedSlotId = item.FeedSlotId,
                SlotLabel = item.SlotLabel,
                SpoolId = item.SpoolId,
                SpoolLabel = item.SpoolLabel,
                InventoryGrams = item.InventoryGrams,
                ReadGrams = item.ReadGrams,
                Material = item.Material,
                ColorName = item.ColorName,
            }).ToList(),
        };
    }
}
