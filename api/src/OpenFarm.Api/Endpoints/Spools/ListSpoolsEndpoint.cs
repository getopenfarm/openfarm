using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class ListSpoolsRequest
{
    public SpoolState? State { get; set; }

    public Guid? FilamentId { get; set; }

    public Guid? FilamentVariantId { get; set; }

    public Guid? VendorId { get; set; }

    /// <summary>Whitespace-separated terms; each must match the label, vendor, material, material type or color name.</summary>
    public string? Search { get; set; }

    /// <summary>1-based. Paging is opt-in: with neither <c>page</c> nor <c>pageSize</c> every match is returned.</summary>
    public int? Page { get; set; }

    /// <summary>1–100; an out-of-range value falls back to 25.</summary>
    public int? PageSize { get; set; }

    /// <summary>Newest registered first, instead of oldest first.</summary>
    public bool? NewestFirst { get; set; }
}

public sealed class ListSpoolsResponse
{
    public IReadOnlyList<SpoolResponse> Spools { get; set; } = [];

    /// <summary>Spools matching the filters across all pages.</summary>
    public int TotalCount { get; set; }
}

/// <summary>FR-015 — filters by state, filament, color (variant), and vendor, free-text search, and opt-in paging; all applied in SQL.</summary>
[Permission("spools:list")]
public sealed class ListSpoolsEndpoint(IQueryHandler<GetSpoolsQuery, SpoolSummaryPage> handler)
    : Endpoint<ListSpoolsRequest, ListSpoolsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/spools");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListSpoolsRequest req, CancellationToken ct)
    {
        var paged = req.Page is not null || req.PageSize is not null;
        var page = paged ? Math.Max(req.Page ?? 1, 1) : (int?)null;
        var pageSize = paged ? (req.PageSize is < 1 or > 100 or null ? 25 : req.PageSize) : null;

        var result = await handler.HandleAsync(
            new GetSpoolsQuery(
                req.State, req.FilamentId, req.FilamentVariantId, req.VendorId, req.Search, Page: page, PageSize: pageSize,
                NewestFirst: req.NewestFirst ?? false), ct);

        Response = new ListSpoolsResponse
        {
            Spools = result.Spools.Select(SpoolResponse.FromSummary).ToList(),
            TotalCount = result.TotalCount,
        };
    }
}
