using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Every colour's stock, with grams used over the last <see cref="UsedDays"/> days.</summary>
public sealed record GetFilamentStockQuery
{
    public const int UsedDays = 30;
}

/// <summary>One colour's <see cref="FilamentStock"/>, the low-stock threshold that applies to it, and the names to show it by.</summary>
public sealed record VariantStock(
    Guid FilamentVariantId,
    Guid FilamentId,
    string VendorName,
    string Material,
    string ColorName,
    string ColorHex,
    FilamentStock Stock,
    decimal? ThresholdGrams)
{
    public bool IsLow => Stock.IsBelow(ThresholdGrams);
}

public sealed class GetFilamentStockQueryHandler(ISpoolReadModel spoolReadModel)
    : IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>>
{
    public Task<IReadOnlyList<VariantStock>> HandleAsync(GetFilamentStockQuery query, CancellationToken cancellationToken) =>
        spoolReadModel.ListStockAsync(DateTimeOffset.UtcNow.AddDays(-GetFilamentStockQuery.UsedDays), cancellationToken);
}
