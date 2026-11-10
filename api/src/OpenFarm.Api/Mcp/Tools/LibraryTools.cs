using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Library;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Library;

namespace OpenFarm.Api.Mcp.Tools;

public static class LibraryTools
{
    private const string FolderArg = "root, or a folder id; omit for every folder";

    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_parts", "List parts",
            "Parts in the library with print estimates, materials and tags, by name. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Only parts with all of these tags")] string[]? tags = null,
                [Description(FolderArg)] string? folder = null,
                [Description("1-based page, default 1")] int? page = null,
                [Description("1–100, default 25")] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                if (folder is not null)
                    await request.Service<ILibraryFolderService>().ValidateDestinationAsync("parts", LibraryFolder.ParseLocation(folder), ct);
                var parts = ListPartsResponse.From(await request.Service<IQueryHandler<GetPartsQuery, PartsPage>>()
                    .HandleAsync(new GetPartsQuery(tags ?? [], McpTools.Page(page), McpTools.PageSize(pageSize), Folder: folder), ct));
                foreach (var part in parts.Parts) part.ThumbnailBase64 = null;
                return parts;
            },
            "parts:list"),
        McpTools.Read("get_part", "Get part",
            "One part: file format, plate, slicer pin, filament requirements and print estimates. File contents are left out.",
            async (RequestContext<CallToolRequestParams> request, [Description("Part id")] Guid partId, CancellationToken ct) =>
            {
                var part = PartResponse.FromDetail(McpTools.Found(await request.Service<IQueryHandler<GetPartQuery, PartDetail?>>()
                    .HandleAsync(new GetPartQuery(partId), ct), "Part"));
                part.Thumbnails = [];
                return part;
            },
            "parts:read"),
        McpTools.Read("list_assemblies", "List assemblies",
            "Assemblies in the library, by name. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description(FolderArg)] string? folder = null,
                [Description("1-based page, default 1")] int? page = null,
                [Description("1–100, default 25")] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                if (folder is not null)
                    await request.Service<ILibraryFolderService>().ValidateDestinationAsync("assemblies", LibraryFolder.ParseLocation(folder), ct);
                return ListAssembliesResponse.From(await request.Service<IQueryHandler<GetAssembliesQuery, AssembliesPage>>()
                    .HandleAsync(new GetAssembliesQuery(McpTools.Page(page), McpTools.PageSize(pageSize), Folder: folder), ct), previews: null);
            },
            "assemblies:view"),
        McpTools.Read("get_assembly", "Get assembly",
            "One assembly and its bill of materials, one level deep; call again with a sub-assembly line's targetId to expand it.",
            async (RequestContext<CallToolRequestParams> request, [Description("Assembly id")] Guid assemblyId, CancellationToken ct) =>
                AssemblyResponse.FromDetail(McpTools.Found(await request.Service<IQueryHandler<GetAssemblyQuery, AssemblyDetail?>>()
                    .HandleAsync(new GetAssemblyQuery(assemblyId), ct), "Assembly")),
            "assemblies:view"),
    ];
}
