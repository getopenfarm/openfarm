using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.NonPrintedComponents;

public sealed class ListNonPrintedComponentsRequest
{
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed class ListNonPrintedComponentsResponse
{
    public List<NonPrintedComponentResponse> Items { get; set; } = [];

    public int TotalCount { get; set; }
}

/// <summary>GET /api/v1/non-printed-components — paginated list, same envelope shape as GET /assemblies (contracts/assembly-bom-api.md).</summary>
[Permission("non-printed-components:view")]
public sealed class ListNonPrintedComponentsEndpoint(IQueryHandler<ListNonPrintedComponentsQuery, NonPrintedComponentsPage> handler)
    : Endpoint<ListNonPrintedComponentsRequest, ListNonPrintedComponentsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/non-printed-components");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListNonPrintedComponentsRequest req, CancellationToken ct)
    {
        var page = Math.Max(req.Page, 1);
        var pageSize = req.PageSize is < 1 or > 200 ? 25 : req.PageSize;

        var result = await handler.HandleAsync(new ListNonPrintedComponentsQuery(page, pageSize, ListSort.Parse(req.SortBy, req.SortDirection, "name")), ct);

        Response = new ListNonPrintedComponentsResponse
        {
            TotalCount = result.TotalCount,
            Items = result.Components.Select(NonPrintedComponentResponse.FromSummary).ToList(),
        };
    }
}
