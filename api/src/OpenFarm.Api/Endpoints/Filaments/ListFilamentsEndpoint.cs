using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class VariantStockResponse
{
    public Guid FilamentVariantId { get; set; }

    /// <summary>Grams left on the colour's unfinished spools.</summary>
    public decimal OnHandGrams { get; set; }

    public int ActiveSpoolCount { get; set; }

    /// <summary>What those spools are worth at each one's own price; partial while <see cref="UnpricedSpoolCount"/> is above 0.</summary>
    public decimal StockValue { get; set; }

    /// <summary>Spools with grams left and no price at any level, left out of <see cref="StockValue"/> rather than counted as free.</summary>
    public int UnpricedSpoolCount { get; set; }

    /// <summary>Grams prints and manual usage took from the colour's spools in the last 30 days.</summary>
    public decimal UsedLast30DaysGrams { get; set; }

    /// <summary>The threshold that applies: the colour's own, else the filament's default once the colour has been stocked.</summary>
    public decimal? ThresholdGrams { get; set; }

    /// <summary>How many days the grams on hand last at the last 30 days' pace; null when the colour was not used.</summary>
    public decimal? DaysLeft { get; set; }

    /// <summary>The days-left threshold that applies, by the same rule as <see cref="ThresholdGrams"/>.</summary>
    public int? ThresholdDays { get; set; }

    /// <summary>Under either threshold.</summary>
    public bool IsLow { get; set; }

    public static VariantStockResponse From(VariantStock stock) => new()
    {
        FilamentVariantId = stock.FilamentVariantId,
        OnHandGrams = stock.Stock.OnHandGrams,
        ActiveSpoolCount = stock.Stock.ActiveSpools,
        StockValue = stock.Stock.StockValue,
        UnpricedSpoolCount = stock.Stock.UnpricedSpools,
        UsedLast30DaysGrams = stock.Stock.UsedGrams,
        ThresholdGrams = stock.ThresholdGrams,
        DaysLeft = stock.DaysLeft,
        ThresholdDays = stock.ThresholdDays,
        IsLow = stock.IsLow,
    };
}

public sealed class ListFilamentsResponse
{
    public IReadOnlyList<FilamentResponse> Filaments { get; set; } = [];

    /// <summary>One entry per colour of every filament listed, apart from the filaments so a save's response cannot reset it.</summary>
    public IReadOnlyList<VariantStockResponse> Stock { get; set; } = [];
}

[Permission("filaments:list")]
public sealed class ListFilamentsEndpoint(
    IQueryHandler<GetFilamentsQuery, IReadOnlyList<FilamentSummary>> handler,
    IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>> stockHandler)
    : EndpointWithoutRequest<ListFilamentsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/filaments");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var filaments = await handler.HandleAsync(new GetFilamentsQuery(), ct);
        var stock = await stockHandler.HandleAsync(new GetFilamentStockQuery(), ct);

        Response = new ListFilamentsResponse
        {
            Filaments = filaments.Select(FilamentResponse.FromSummary).ToList(),
            Stock = stock.Select(VariantStockResponse.From).ToList(),
        };
    }
}
