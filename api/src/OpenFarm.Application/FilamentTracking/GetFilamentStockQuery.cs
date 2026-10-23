using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Every colour's stock, with grams used over the last <see cref="UsedDays"/> days.</summary>
public sealed record GetFilamentStockQuery
{
    public const int UsedDays = 30;
}

/// <summary>One colour's <see cref="FilamentStock"/>, the low-stock thresholds that apply to it, and the names to show it by.</summary>
public sealed record VariantStock(
    Guid FilamentVariantId,
    Guid FilamentId,
    string VendorName,
    string Material,
    string MaterialType,
    string ColorName,
    string ColorHex,
    FilamentStock Stock,
    decimal? ThresholdGrams,
    int? ThresholdDays = null)
{
    /// <summary>How long the stock lasts at the pace of the last <see cref="GetFilamentStockQuery.UsedDays"/> days; null without use.</summary>
    public decimal? DaysLeft => Stock.DaysLeft(GetFilamentStockQuery.UsedDays);

    public bool IsLow => Stock.IsLow(ThresholdGrams, ThresholdDays, GetFilamentStockQuery.UsedDays);
}

public sealed class GetFilamentStockQueryHandler(ISpoolReadModel spoolReadModel)
    : IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>>
{
    public Task<IReadOnlyList<VariantStock>> HandleAsync(GetFilamentStockQuery query, CancellationToken cancellationToken) =>
        spoolReadModel.ListStockAsync(DateTimeOffset.UtcNow.AddDays(-GetFilamentStockQuery.UsedDays), cancellationToken);
}
