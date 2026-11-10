using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Reports;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Production;
using OpenFarm.Application.Reports;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Mcp.Tools;

public static class FilamentTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_filaments", "List filaments",
            "Every filament and its colours, with stock per colour: grams on hand, use over the last 30 days, days left and low-stock state.",
            async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
                ListFilamentsResponse.From(
                    await request.Service<IQueryHandler<GetFilamentsQuery, IReadOnlyList<FilamentSummary>>>().HandleAsync(new GetFilamentsQuery(), ct),
                    await request.Service<IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>>>().HandleAsync(new GetFilamentStockQuery(), ct)),
            "filaments:list"),
        McpTools.Read("list_filament_shortages", "List filament shortages",
            "Colours that queued, printing and paused jobs need more grams of than are on hand.",
            async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
                ListFilamentShortagesResponse.From(await request.Service<IQueryHandler<GetFilamentShortagesQuery, IReadOnlyList<FilamentShortage>>>()
                    .HandleAsync(new GetFilamentShortagesQuery(), ct)),
            "filaments:list"),
        McpTools.Read("list_spools", "List spools",
            "Spools with filament, colour, grams left and where each is loaded, oldest registered first. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description("InStorage, Using or Finished")] string? state = null,
                [Description("Filament id")] Guid? filamentId = null,
                [Description("Filament colour (variant) id")] Guid? filamentVariantId = null,
                [Description("Vendor id")] Guid? vendorId = null,
                [Description("Whitespace-separated terms; each must match the label, vendor, material, material type or colour name")] string? search = null,
                [Description("Newest registered first")] bool? newestFirst = null,
                [Description("1-based page, default 1")] int? page = null,
                [Description("1–100, default 25")] int? pageSize = null,
                CancellationToken ct = default) =>
                ListSpoolsResponse.From(await request.Service<IQueryHandler<GetSpoolsQuery, SpoolSummaryPage>>().HandleAsync(new GetSpoolsQuery(
                    state is null ? null : Enum.Parse<SpoolState>(state, ignoreCase: true), filamentId, filamentVariantId, vendorId, search,
                    Page: McpTools.Page(page), PageSize: McpTools.PageSize(pageSize), NewestFirst: newestFirst ?? false), ct)),
            "spools:list"),
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
}
