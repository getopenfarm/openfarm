using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Application.Production;

/// <param name="CoverDays">Days of use the suggested purchase covers on top of queued work.</param>
public sealed record GetFilamentForecastQuery(int CoverDays = 30);

/// <summary>The newest spool registered of a colour: what was bought last, from whom, and in what size.</summary>
public sealed record SpoolPurchase(Guid FilamentVariantId, decimal SpoolWeightGrams, decimal? Price, string? Supplier, DateTimeOffset RegisteredAtUtc);

/// <summary>One colour's stock against queued work and its recent pace, with what to buy of it.</summary>
/// <param name="QueuedGrams">Grams of queued work tied to this exact colour; only its spools can print it.</param>
/// <param name="SharedQueuedGrams">Grams of queued work that any brand of this material and colour can print; the same figure on each brand, counted once.</param>
/// <param name="ShortGrams">Grams of queued work that no spool on hand can cover, assigned to the colour that should be bought.</param>
/// <param name="NeededGrams">Grams to buy of this colour so stock covers queued work plus the cover days of use.</param>
/// <param name="RunsOutOn">When this material and colour runs out across brands at the recent pace; today when its own queued work is already short.</param>
public sealed record FilamentForecast(VariantStock Stock, decimal QueuedGrams, decimal SharedQueuedGrams, SpoolPurchase? LastPurchase,
    decimal AveragePerDayGrams, decimal ShortGrams, decimal NeededGrams, int SuggestedSpools, DateOnly? RunsOutOn);

/// <summary>
/// Purchases per material and colour. A colour's own jobs (tied to it) need its spools; whatever every brand has left forms
/// one pool for shared jobs and the next days of use, and any shortfall of that pool goes on the brand bought last (most
/// stock when none was bought), so one need is never suggested once per brand.
/// ponytail: a linear 30-day pace, finite work only (backfill is unbounded), and past use counted as interchangeable between
/// brands; weight weekdays or keep per-brand pace if forecasts miss.
/// </summary>
public static class FilamentForecasts
{
    /// <summary>Beyond this, "runs out" carries no buying signal (and a far date would overflow).</summary>
    public const int MaxRunOutDays = 3650;

    private const decimal AssumedSpoolGrams = 1000m;

    public static IReadOnlyList<FilamentForecast> Compute(IReadOnlyList<VariantStock> stock, QueuedDemand queued,
        IReadOnlyDictionary<Guid, SpoolPurchase> purchases, int coverDays, DateOnly today)
    {
        var result = new List<FilamentForecast>(stock.Count);
        foreach (var group in stock.GroupBy(s => ColourKey.Of(s.MaterialType, s.ColorHex)))
        {
            var colours = group.Select(s => new
            {
                Stock = s,
                Own = queued.ByVariant.GetValueOrDefault(s.FilamentVariantId),
                Daily = s.Stock.UsedGrams / GetFilamentStockQuery.UsedDays,
                Purchase = purchases.GetValueOrDefault(s.FilamentVariantId),
            }).ToList();
            var shared = queued.ByColour.GetValueOrDefault(group.Key);
            var spare = colours.Sum(c => Math.Max(0, c.Stock.Stock.OnHandGrams - c.Own));
            var daily = colours.Sum(c => c.Daily);
            var sharedShort = Math.Max(0, shared - spare);
            var sharedNeed = Math.Max(0, shared + (coverDays * daily) - spare);
            var buyer = colours.OrderByDescending(c => c.Purchase?.RegisteredAtUtc ?? DateTimeOffset.MinValue)
                .ThenByDescending(c => c.Stock.Stock.OnHandGrams).ThenBy(c => c.Stock.FilamentVariantId).First().Stock.FilamentVariantId;
            var poolRunsOut = RunsOut(spare - shared, daily, today);
            foreach (var c in colours)
            {
                var ownShort = Math.Max(0, c.Own - c.Stock.Stock.OnHandGrams);
                var buys = c.Stock.FilamentVariantId == buyer;
                var need = ownShort + (buys ? sharedNeed : 0);
                var spool = c.Purchase?.SpoolWeightGrams is > 0 and var grams ? grams : AssumedSpoolGrams;
                result.Add(new FilamentForecast(c.Stock, c.Own, shared, c.Purchase, Math.Round(c.Daily, 1),
                    ownShort + (buys ? sharedShort : 0), need, (int)Math.Ceiling(need / spool), ownShort > 0 ? today : poolRunsOut));
            }
        }
        return result;
    }

    private static DateOnly? RunsOut(decimal remainingGrams, decimal dailyGrams, DateOnly today)
    {
        if (dailyGrams <= 0) return null;
        var days = Math.Floor(Math.Max(0, remainingGrams) / dailyGrams);
        return days > MaxRunOutDays ? null : today.AddDays((int)days);
    }
}

public sealed class GetFilamentForecastQueryHandler(
    IPrintJobRepository jobs, IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>> stockQuery, ISpoolReadModel spools)
    : IQueryHandler<GetFilamentForecastQuery, IReadOnlyList<FilamentForecast>>
{
    public async Task<IReadOnlyList<FilamentForecast>> HandleAsync(GetFilamentForecastQuery query, CancellationToken cancellationToken)
    {
        var stock = await stockQuery.HandleAsync(new GetFilamentStockQuery(), cancellationToken);
        var queued = await GetFilamentShortagesQueryHandler.QueuedDemandAsync(jobs, cancellationToken);
        var purchases = (await spools.ListLatestPurchasesAsync(cancellationToken)).ToDictionary(p => p.FilamentVariantId);
        return FilamentForecasts.Compute(stock, queued, purchases, query.CoverDays, DateOnly.FromDateTime(DateTime.UtcNow));
    }
}
