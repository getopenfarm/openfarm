using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class ListAssembliesRequest
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed class ListAssembliesResponse
{
    public List<AssemblySummaryResponse> Items { get; set; } = [];

    public int TotalCount { get; set; }
}

/// <summary>GET /api/v1/assemblies — paginated list (contracts/assembly-bom-api.md).</summary>
[Permission("assemblies:view")]
public sealed class ListAssembliesEndpoint(IQueryHandler<GetAssembliesQuery, AssembliesPage> handler)
    : Endpoint<ListAssembliesRequest, ListAssembliesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/assemblies");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListAssembliesRequest req, CancellationToken ct)
    {
        var page = Math.Max(req.Page, 1);
        var pageSize = req.PageSize is < 1 or > 200 ? 25 : req.PageSize;

        var result = await handler.HandleAsync(new GetAssembliesQuery(page, pageSize), ct);

        Response = new ListAssembliesResponse
        {
            TotalCount = result.TotalCount,
            Items = result.Assemblies.Select(AssemblySummaryResponse.FromSummary).ToList(),
        };
    }
}
