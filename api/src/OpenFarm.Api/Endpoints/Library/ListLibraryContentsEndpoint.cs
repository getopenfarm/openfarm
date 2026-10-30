using FastEndpoints;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Library;
using OpenFarm.Domain.Library;

namespace OpenFarm.Api.Endpoints.Library;

public sealed class ListLibraryContentsRequest : FolderLibraryRequest
{
    public Guid? ParentId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
    public bool? IncludePreviews { get; set; }
}
public sealed record LibraryEntryResponse(Guid Id, string Name, string Kind, LibraryFolder? Folder = null, PartSummaryResponse? Part = null, AssemblySummaryResponse? Assembly = null);
public sealed record LibraryContentsResponse(IReadOnlyList<LibraryEntryResponse> Items, int TotalCount, IReadOnlyList<LibraryFolder> Ancestors);

[Permission("library:view")]
public sealed class ListLibraryContentsEndpoint(ILibraryContentsReader reader, IAssemblyPreviewReader previews) : Endpoint<ListLibraryContentsRequest, LibraryContentsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/library/{library}/contents");
        Policies("Permission");
    }
    public override async Task HandleAsync(ListLibraryContentsRequest req, CancellationToken ct)
    {
        var result = await reader.ReadAsync(req.Library, req.ParentId, Math.Max(1, req.Page), req.PageSize is < 1 or > 200 ? 25 : req.PageSize, ListSort.Parse(req.SortBy, req.SortDirection, "name"), ct);
        var images = req.IncludePreviews == true && req.Library == "assemblies"
            ? await previews.ReadAsync(result.Items.Where(e => e.Kind == "assembly").Select(e => e.Id).ToList(), ct) : null;
        Response = new(result.Items.Select(entry =>
        {
            var assembly = entry.Assembly is null ? null : AssemblySummaryResponse.FromSummary(entry.Assembly);
            if (assembly is not null) assembly.PreviewThumbnails = images?.GetValueOrDefault(entry.Id) ?? [];
            return new LibraryEntryResponse(entry.Id, entry.Name, entry.Kind, entry.Folder, entry.Part is null ? null : PartSummaryResponse.FromSummary(entry.Part), assembly);
        }).ToList(), result.TotalCount, result.Ancestors);
    }
}
