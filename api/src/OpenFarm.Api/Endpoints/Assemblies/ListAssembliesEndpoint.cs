using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class ListAssembliesRequest
{
    public bool? IncludePreviews { get; set; }

    public string? Folder { get; set; }

    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }

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
public sealed class ListAssembliesEndpoint(IQueryHandler<GetAssembliesQuery, AssembliesPage> handler, OpenFarm.Application.Library.ILibraryFolderService folders, OpenFarm.Application.Library.IAssemblyPreviewReader previews)
    : Endpoint<ListAssembliesRequest, ListAssembliesResponse>
{
    private static Guid? LibraryLocation(string folder) => OpenFarm.Domain.Library.LibraryFolder.ParseLocation(folder);

    public override void Configure()
    {
        Get("/api/v1/assemblies");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListAssembliesRequest req, CancellationToken ct)
    {
        if (req.Folder is not null)
            await folders.ValidateDestinationAsync("assemblies", LibraryLocation(req.Folder), ct);
        var page = Math.Max(req.Page, 1);
        var pageSize = req.PageSize is < 1 or > 200 ? 25 : req.PageSize;

        var result = await handler.HandleAsync(new GetAssembliesQuery(page, pageSize, ListSort.Parse(req.SortBy, req.SortDirection, "name"), req.Folder), ct);

        var images = req.IncludePreviews == true ? await previews.ReadAsync(result.Assemblies.Select(a => a.Id).ToList(), ct) : null;
        Response = new ListAssembliesResponse
        {
            TotalCount = result.TotalCount,
            Items = result.Assemblies.Select(a => { var item = AssemblySummaryResponse.FromSummary(a); item.PreviewThumbnails = images?.GetValueOrDefault(a.Id) ?? []; return item; }).ToList(),
        };
    }
}
