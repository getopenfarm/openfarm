using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.FilamentTracking;

/// <param name="Page">1-based.</param>
/// <param name="PageSize">1–<see cref="MaxPageSize"/>; larger values are clamped.</param>
public sealed record GetSpoolHistoryQuery(Guid SpoolId, int Page = 1, int PageSize = GetSpoolHistoryQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
}

/// <summary>
/// One change to a spool's remaining weight. <see cref="GramsConsumed"/> is grams taken off the spool (negative when a
/// correction put filament back); <see cref="RemainingAfterGrams"/> is null on entries older than the ledger's weight column.
/// The print columns are null when the entry has no print, or the print is gone.
/// </summary>
public sealed record SpoolHistoryEntry(
    Guid Id,
    DateTimeOffset RecordedAtUtc,
    decimal GramsConsumed,
    decimal? RemainingAfterGrams,
    SpoolUsageSource Source,
    string? Reason,
    Guid? PrintId,
    string? PrintName,
    PrintJobStatus? PrintStatus);

/// <summary>One page of a spool's history, newest first, and the number of entries across all pages.</summary>
public sealed record SpoolHistoryPage(IReadOnlyList<SpoolHistoryEntry> Entries, int TotalCount);

public sealed class GetSpoolHistoryQueryHandler(ISpoolReadModel spoolReadModel) : IQueryHandler<GetSpoolHistoryQuery, SpoolHistoryPage>
{
    public async Task<SpoolHistoryPage> HandleAsync(GetSpoolHistoryQuery query, CancellationToken cancellationToken) =>
        await spoolReadModel.ListHistoryAsync(query, cancellationToken) ?? throw new SpoolNotFoundException(query.SpoolId);
}
