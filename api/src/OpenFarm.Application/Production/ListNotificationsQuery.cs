using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record ListNotificationsQuery(int Limit = 50);

/// <summary>One filament line automatic deduction left unrecorded; <see cref="Reason"/> says whether a spool choice can fix it.</summary>
public sealed record NotificationLine(
    Guid LineId, int ToolIndex, string? MaterialType, string? ColorHex, decimal? EstimatedGrams, Guid? SpoolId, FilamentSkipReason Reason);

/// <summary>An action-required item. <see cref="Type"/> is "FilamentNotDeducted" today; <see cref="PrintId"/> identifies it.</summary>
public sealed record NotificationItem(
    string Type, Guid PrintId, string Name, PrintSource Source, PrintJobStatus Status, Guid? PrinterId,
    DateTimeOffset? EndedAtUtc, IReadOnlyList<NotificationLine> Lines);

/// <summary>A colour whose grams on hand dropped under its low-stock threshold; it clears itself once restocked.</summary>
public sealed record LowStockItem(
    Guid FilamentId, Guid FilamentVariantId, string VendorName, string Material, string ColorName, string ColorHex,
    decimal OnHandGrams, decimal ThresholdGrams);

/// <summary><see cref="TotalCount"/> counts <see cref="Items"/> across pages; <see cref="LowStock"/> is listed whole.</summary>
public sealed record NotificationsPage(IReadOnlyList<NotificationItem> Items, int TotalCount, IReadOnlyList<LowStockItem> LowStock);

public sealed class ListNotificationsQueryHandler(
    IPrintRepository prints, IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>> stock)
    : IQueryHandler<ListNotificationsQuery, NotificationsPage>
{
    public const string FilamentNotDeducted = "FilamentNotDeducted";

    public async Task<NotificationsPage> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        var (found, total) = await prints.ListNeedingFilamentFollowUpAsync(query.Limit, cancellationToken);
        var items = found.Select(print => new NotificationItem(
            FilamentNotDeducted, print.Id, print.Name, print.Source, print.Status, print.PrinterId, print.EndedAtUtc,
            print.Filaments
                .Where(f => f.SkippedReason is not null)
                .OrderBy(f => f.ToolIndex)
                .Select(f => new NotificationLine(f.Id, f.ToolIndex, f.MaterialType, f.DetectedColorHex, f.EstimatedGrams, f.SpoolId, f.SkippedReason!.Value))
                .ToList())).ToList();
        var lowStock = (await stock.HandleAsync(new GetFilamentStockQuery(), cancellationToken))
            .Where(s => s.IsLow)
            .OrderBy(s => s.VendorName).ThenBy(s => s.Material).ThenBy(s => s.ColorName)
            .Select(s => new LowStockItem(
                s.FilamentId, s.FilamentVariantId, s.VendorName, s.Material, s.ColorName, s.ColorHex, s.Stock.OnHandGrams, s.ThresholdGrams!.Value))
            .ToList();
        return new NotificationsPage(items, total, lowStock);
    }
}
