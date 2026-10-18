using OpenFarm.Application.Common;
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

public sealed record NotificationsPage(IReadOnlyList<NotificationItem> Items, int TotalCount);

public sealed class ListNotificationsQueryHandler(IPrintRepository prints) : IQueryHandler<ListNotificationsQuery, NotificationsPage>
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
        return new NotificationsPage(items, total);
    }
}
