using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Reports;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Production;
using OpenFarm.Application.Reports;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="FilamentVariantId">The colour's id; register_spools takes it.</param>
/// <param name="ActiveSpools">Spools not finished: in storage or loaded.</param>
/// <param name="UsedLast30DaysGrams">Grams prints and manual usage took in the last 30 days.</param>
/// <param name="QueuedGrams">Grams of unfinished finite jobs tied to this exact colour: only its spools can print them.</param>
/// <param name="SharedQueuedGrams">Grams of unfinished finite jobs that name only this material and colour: any brand of it can print them.
/// Every brand shows the same figure; it is counted once. Unlimited (backfill) work is not counted.</param>
/// <param name="ShortGrams">Grams of queued work no spool on hand can cover, after every brand of the colour is used.</param>
/// <param name="RunsOutOn">When this material and colour runs out across brands at the last 30 days' pace after queued work; today when
/// work tied to this colour is already short; empty without recent use or beyond ten years.</param>
/// <param name="NeededGrams">Grams to buy of this colour: its own shortfall, plus the colour's shared need on the brand bought last.</param>
/// <param name="SuggestedSpools">Spools of the last size bought (1 kg when none) that cover NeededGrams.</param>
/// <param name="LastPrice">The price paid per spool for the newest spool, in the farm currency.</param>
public sealed record McpFilamentColour(Guid FilamentVariantId, Guid FilamentId, string Name, string MaterialType, string ColorHex,
    decimal OnHandGrams, int ActiveSpools, decimal UsedLast30DaysGrams, decimal AveragePerDayGrams, decimal QueuedGrams, decimal SharedQueuedGrams,
    decimal ShortGrams, DateOnly? RunsOutOn, bool LowStock, decimal? LowStockGrams, int? LowStockDays,
    decimal NeededGrams, int SuggestedSpools, decimal? LastSpoolGrams, decimal? LastPrice, string? LastSupplier, DateTimeOffset? LastBoughtUtc);

public sealed record McpFilamentForecast(int CoverDays, IReadOnlyList<McpFilamentColour> Colours);

public sealed record McpSpoolLine(
    [property: Description("Colour id (filamentVariantId) from list_filaments")] Guid FilamentVariantId,
    [property: Description("How many identical spools, 1–500")] int Quantity,
    [property: Description("Net filament grams of a full spool, such as 1000")] decimal SpoolWeightGrams,
    [property: Description("Price per spool in the farm currency; omit when unknown")] decimal? Price = null,
    [property: Description("Grams left when the spools are not full; omit for full spools")] decimal? RemainingGrams = null,
    [property: Description("A label printed or written on the spools")] string? Label = null);

public sealed record McpRegisteredSpools(int Count, IReadOnlyList<string> Colours);

public sealed record McpSpoolsPage(IReadOnlyList<SpoolResponse> Spools, int Page, int PageSize, int TotalCount, bool HasMore);

public static class FilamentTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_filaments", "List filament stock and forecast",
            "Every filament colour with grams on hand, the last 30 days' use, grams queued work still needs, when it runs out, and how many spools to buy "
            + "to cover queued work plus coverDays of use, with the last price and supplier. Use it to plan purchases and to find colour ids for register_spools. "
            + "Brands of one material and colour share stock for work that names only the colour, so each need is suggested once, on the brand bought last.",
            async (RequestContext<CallToolRequestParams> request,
                [Description("Whitespace-separated terms; each must match the vendor, material, material type or colour name")] string? search = null,
                [Description("Only colours that are low, short for queued work, or need buying")] bool atRiskOnly = false,
                [Description("Days of use the suggested purchase should cover, 1–365, default 30")] int coverDays = 30,
                CancellationToken ct = default) =>
            {
                if (coverDays is < 1 or > 365) throw new ArgumentException("coverDays must be 1–365.");
                var terms = (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var colours = (await request.Service<IQueryHandler<GetFilamentForecastQuery, IReadOnlyList<FilamentForecast>>>()
                        .HandleAsync(new GetFilamentForecastQuery(coverDays), ct))
                    .Where(f => terms.All(t => new[] { f.Stock.VendorName, f.Stock.Material, f.Stock.MaterialType, f.Stock.ColorName }
                        .Any(v => v.Contains(t, StringComparison.OrdinalIgnoreCase))))
                    .Select(f => new McpFilamentColour(f.Stock.FilamentVariantId, f.Stock.FilamentId, Name(f.Stock), f.Stock.MaterialType, f.Stock.ColorHex,
                        f.Stock.Stock.OnHandGrams, f.Stock.Stock.ActiveSpools, f.Stock.Stock.UsedGrams, f.AveragePerDayGrams, f.QueuedGrams, f.SharedQueuedGrams,
                        f.ShortGrams, f.RunsOutOn, f.Stock.IsLow, f.Stock.ThresholdGrams, f.Stock.ThresholdDays,
                        Math.Round(f.NeededGrams), f.SuggestedSpools,
                        f.LastPurchase?.SpoolWeightGrams, f.LastPurchase?.Price, f.LastPurchase?.Supplier, f.LastPurchase?.RegisteredAtUtc))
                    .Where(c => !atRiskOnly || c.LowStock || c.ShortGrams > 0 || c.SuggestedSpools > 0)
                    .OrderBy(c => c.RunsOutOn ?? DateOnly.MaxValue).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new McpFilamentForecast(coverDays, colours);
            },
            "filaments:list", "spools:list"),
        McpTools.Read("list_spools", "List spools",
            "Spools with filament, colour, grams left and where each is loaded, oldest registered first. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description("InStorage, Using or Finished")] string? state = null,
                [Description("Filament colour (variant) id")] Guid? filamentVariantId = null,
                [Description("Whitespace-separated terms; each must match the label, vendor, material, material type or colour name")] string? search = null,
                [Description("Newest registered first")] bool? newestFirst = null,
                [Description(McpTools.PageArg)] int? page = null,
                [Description(McpTools.PageSizeArg)] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                var (p, size) = (McpTools.Page(page), McpTools.PageSize(pageSize));
                var spools = ListSpoolsResponse.From(await request.Service<IQueryHandler<GetSpoolsQuery, SpoolSummaryPage>>().HandleAsync(new GetSpoolsQuery(
                    state is null ? null : Enum.TryParse<SpoolState>(state, ignoreCase: true, out var s) && Enum.IsDefined(s) ? s
                        : throw new ArgumentException("state must be InStorage, Using or Finished."),
                    null, filamentVariantId, null, search, Page: p, PageSize: size, NewestFirst: newestFirst ?? false), ct));
                return new McpSpoolsPage(spools.Spools, p, size, spools.TotalCount, p * size < spools.TotalCount);
            },
            "spools:list"),
        McpTools.Write("register_spools", "Register spools",
            "Adds a delivery of new spools to stock, all or none: one line per colour and size. Find colour ids with list_filaments; "
            + "a colour not listed must first be added to its filament in OpenFarm by a person.",
            destructive: false, idempotent: false, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("1–20 lines")] McpSpoolLine[] lines,
                [Description("Who the spools were bought from")] string? supplier = null,
                [Description("The order or invoice number")] string? orderNumber = null,
                [Description("Date received, YYYY-MM-DD; default today")] DateOnly? receivedOn = null,
                CancellationToken ct = default) =>
            {
                var dto = new RegisterSpoolRequest
                {
                    Lines = [.. (lines ?? []).Select(l => new RegisterSpoolLineRequest
                    {
                        FilamentVariantId = l.FilamentVariantId, Quantity = l.Quantity, SpoolWeightGrams = l.SpoolWeightGrams,
                        Price = l.Price, RemainingWeightGrams = l.RemainingGrams, Label = l.Label,
                    })],
                    Supplier = supplier, OrderNumber = orderNumber, ReceivedOn = receivedOn,
                };
                var response = await request.WriteAsync(dto, async () =>
                {
                    var spools = await request.Service<ICommandHandler<RegisterSpoolCommand, IReadOnlyList<Spool>>>().HandleAsync(new RegisterSpoolCommand(
                        dto.Lines.Select(l => new RegisterSpoolLine(l.FilamentVariantId, l.SpoolWeightGrams, l.Quantity, l.RemainingWeightGrams, l.Price, l.Label)).ToList(),
                        dto.Supplier, dto.OrderNumber, dto.ReceivedOn, null), ct);
                    return new RegisterSpoolsResponse
                    {
                        Spools = await SpoolResponseLookup.ResolveManyAsync(request.Service<IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>>>(),
                            spools.Select(s => s.Id).ToList(), ct),
                    };
                }, ct);
                return new McpRegisteredSpools(response.Spools.Count, response.Spools
                    .GroupBy(s => $"{s.VendorName} {s.Material} · {s.ColorName}")
                    .Select(g => $"{g.Count()} × {g.Key}").ToList());
            },
            "spools:register"),
        McpTools.Read("get_farm_stats", "Get farm stats",
            "Prints, filament used, printer time and failures over a range of at most 366 days, against the range before it.",
            async (RequestContext<CallToolRequestParams> request,
                [Description("Inclusive start: a local midnight, as an instant")] DateTimeOffset from,
                [Description("Exclusive end")] DateTimeOffset to,
                [Description("IANA time zone the days are counted in, such as Asia/Kuala_Lumpur")] string timeZone,
                CancellationToken ct) =>
            {
                new StatsRequestValidator().ValidateAndThrow(new StatsRequest { From = from, To = to, TimeZone = timeZone });
                return await request.Service<IQueryHandler<GetStatsQuery, StatsReport>>()
                    .HandleAsync(new GetStatsQuery(from, to, TimeZoneInfo.FindSystemTimeZoneById(timeZone)), ct);
            },
            "spools:list"),
    ];

    /// <summary>"Bambu Lab PLA Basic · Black".</summary>
    public static string Name(VariantStock s) =>
        $"{string.Join(' ', new[] { s.VendorName, s.Material }.Where(v => !string.IsNullOrWhiteSpace(v)))} · {s.ColorName}";
}
