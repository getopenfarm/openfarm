using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Library;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Library;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Library;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="Kind">part, assembly or folder.</param>
/// <param name="Folder">The folder path it sits in, such as "Storage/Gridfinity"; empty at the root.</param>
/// <param name="Plates">Parts: plates in one print; hours and grams are per plate.</param>
/// <param name="Lines">Assemblies: lines in the bill of materials.</param>
public sealed record McpLibraryItem(string Kind, Guid Id, string Name, string Folder, IReadOnlyList<string>? Tags = null,
    int? UnitsPerPlate = null, decimal? Hours = null, decimal? Grams = null, IReadOnlyList<string>? Materials = null, int? Lines = null);

/// <param name="Folders">Page 1 only: the subfolders when browsing, or the folders whose name matches when searching.</param>
/// <param name="Items">Parts or assemblies; page, pageSize, totalCount and hasMore count these.</param>
public sealed record McpLibraryPage(string Library, IReadOnlyList<McpLibraryItem> Folders, IReadOnlyList<McpLibraryItem> Items, int Page, int PageSize,
    int TotalCount, bool HasMore);

public sealed record McpFolder(Guid FolderId, string Library, string Path);

public sealed record McpUpdatedParts(IReadOnlyList<McpLibraryItem> Parts);

public sealed record McpPartUpdate(
    [property: Description("Part id from openfarm_search_library")] Guid PartId,
    [property: Description("New name; omit to keep")] string? Name = null,
    [property: Description("Tags to add; new tags are created")] string[]? AddTags = null,
    [property: Description("Tags to remove")] string[]? RemoveTags = null);

public sealed record McpBomLine(
    [property: Description("Part, Assembly or NonPrintedComponent")] string Kind,
    [property: Description("The part, sub-assembly or component id")] Guid Id,
    [property: Description("Quantity per assembly; 0 removes the line when updating")] decimal Quantity);

public sealed record McpAssemblyLine(string Kind, Guid Id, string Name, decimal Quantity);

/// <param name="Pricing">The saved pricing rule: mode Price (fixed amount), Margin or Markup (percent), and its value.</param>
/// <param name="Parts">With expand: every printed part in the full tree, with quantity per assembly.</param>
/// <param name="Components">With expand: every non-printed component in the full tree.</param>
public sealed record McpAssembly(Guid AssemblyId, string Name, string? Description, string Folder, IReadOnlyList<McpAssemblyLine> Lines,
    AssemblyPricingDto? Pricing, IReadOnlyList<McpAssemblyLine>? Parts, IReadOnlyList<McpAssemblyLine>? Components);

public static class LibraryTools
{
    private const string LibraryArg = "parts or assemblies";

    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("openfarm_search_library", "Search library",
            "Finds parts and assemblies by name (and parts by tag), with their folder path; or, with no query, lists one folder's subfolders and items. "
            + "Use it to find ids for openfarm_plan_production, openfarm_get_part, openfarm_get_assembly and the library changes. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description(LibraryArg)] string library,
                [Description("Whitespace-separated terms; each must match the name, or a tag of a part. Omit to browse a folder")] string? query = null,
                [Description("Folder id to browse, or to search directly in (not its subfolders); omit for the root when browsing, or every folder when searching")] Guid? folderId = null,
                [Description("Parts only: only parts with all of these tags")] string[]? tags = null,
                [Description(McpTools.PageArg)] int? page = null,
                [Description(McpTools.PageSizeArg)] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                library = Library(library);
                await request.RequireAsync(library == "parts" ? "parts:list" : "assemblies:view");
                var (p, size) = (McpTools.Page(page), McpTools.PageSize(pageSize));
                var folders = request.Service<ILibraryFolderService>();
                await folders.ValidateDestinationAsync(library, folderId, ct);
                var paths = Paths(await folders.ListAllAsync(library, ct));
                if (tags is { Length: > 0 } && library != "parts") throw new ArgumentException("tags filter parts only; assemblies have no tags.");
                var browsing = string.IsNullOrWhiteSpace(query) && tags is not { Length: > 0 };
                var location = folderId?.ToString() ?? (browsing ? "root" : null);
                var folderRows = p > 1 ? [] : paths
                    .Where(f => browsing ? f.Value.ParentId == folderId : (folderId is null || f.Value.ParentId == folderId) && LikeAll(f.Value.Name, query))
                    .OrderBy(f => f.Value.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new McpLibraryItem("folder", f.Key, f.Value.Name, Parent(f.Value.Path))).ToList();
                List<McpLibraryItem> items;
                int total;
                if (library == "parts")
                {
                    var parts = await request.Service<IQueryHandler<GetPartsQuery, PartsPage>>().HandleAsync(
                        new GetPartsQuery(tags ?? [], p, size, ListSort.Parse("name", "asc", "name"), location, query), ct);
                    total = parts.TotalCount;
                    items = [.. parts.Parts.Select(part => new McpLibraryItem("part", part.Id, part.Name, Path(paths, part.FolderId), part.Tags,
                        part.UnitsPerPrint, part.EstimatedPrintTimeMinutes / 60m, part.EstimatedFilamentWeightGrams, part.MaterialTypes))];
                }
                else
                {
                    var assemblies = await request.Service<IQueryHandler<GetAssembliesQuery, AssembliesPage>>().HandleAsync(
                        new GetAssembliesQuery(p, size, ListSort.Parse("name", "asc", "name"), location, query), ct);
                    total = assemblies.TotalCount;
                    items = [.. assemblies.Assemblies.Select(a => new McpLibraryItem("assembly", a.Id, a.Name, Path(paths, a.FolderId), Lines: a.LineCount))];
                }
                return new McpLibraryPage(library, folderRows, items, p, size, total, p * size < total);
            },
            "library:view"),
        McpTools.Read("openfarm_get_part", "Get part",
            "One part: file format, plate, units per plate, slicer pin, filament requirements and print estimates. File contents are left out.",
            async (RequestContext<CallToolRequestParams> request, [Description("Part id")] Guid partId, CancellationToken ct) =>
            {
                var part = PartResponse.FromDetail(await PartAsync(request, partId, ct));
                part.Thumbnails = [];
                return part;
            },
            "parts:read"),
        McpTools.Read("openfarm_get_assembly", "Get assembly",
            "One assembly: its bill of materials one level deep with names, its folder and pricing rule. With expand, also every printed part and "
            + "non-printed component in the full tree, with quantities per assembly.",
            async (RequestContext<CallToolRequestParams> request, [Description("Assembly id")] Guid assemblyId,
                [Description("Also list every part and component in the full tree")] bool expand = false, CancellationToken ct = default) =>
            {
                var assembly = await AssemblyAsync(request, assemblyId, ct);
                var paths = await request.Service<ILibraryFolderService>().ListAllAsync("assemblies", ct);
                BomExplosionResult? tree = expand
                    ? await request.Service<IQueryHandler<ExplodeAssemblyQuery, BomExplosionResult>>().HandleAsync(new ExplodeAssemblyQuery(assemblyId, 1), ct)
                    : null;
                return new McpAssembly(assembly.Id, assembly.Name, assembly.Description, Path(Paths(paths), assembly.FolderId),
                    assembly.Lines.Select(l => new McpAssemblyLine(l.TargetKind.ToString(), l.TargetId, l.TargetName, l.Quantity)).ToList(),
                    AssemblyPricingDto.From(assembly.Pricing),
                    tree?.PrintedParts.Select(x => new McpAssemblyLine("Part", x.PartId, x.PartName, x.Quantity)).ToList(),
                    tree?.NonPrintedComponents.Select(x => new McpAssemblyLine("NonPrintedComponent", x.NonPrintedComponentId, x.Name, x.Quantity)).ToList());
            },
            "assemblies:view"),
        McpTools.Write("openfarm_create_library_folder", "Create library folder",
            "Creates a folder path such as \"Storage/Gridfinity\", making each missing folder along it. An existing path is returned as it is.",
            destructive: false, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description(LibraryArg)] string library,
                [Description("Folder names separated by /, from the root")] string path,
                CancellationToken ct = default) =>
            {
                library = Library(library);
                await request.RequireAsync(library == "parts" ? "parts:list" : "assemblies:view");
                var names = (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (names.Length is 0 or > 10) throw new ArgumentException("path must name 1–10 folders, such as \"Storage/Gridfinity\".");
                var folders = request.Service<ILibraryFolderService>();
                var existing = await folders.ListAllAsync(library, ct);
                Guid? parent = null;
                foreach (var name in names)
                {
                    var found = existing.FirstOrDefault(f => f.ParentId == parent && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                    parent = found?.Id ?? (await request.WriteAsync(new CreateFolderRequest { Library = library, Name = name, ParentId = parent },
                        () => folders.CreateAsync(library, name, parent, ct), ct)).Id;
                }
                return new McpFolder(parent!.Value, library, Path(Paths(await folders.ListAllAsync(library, ct)), parent));
            },
            "library:manage"),
        McpTools.Write("openfarm_rename_library_folder", "Rename library folder",
            "Renames one folder. Names are unique within their parent folder.",
            destructive: false, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description(LibraryArg)] string library,
                [Description("Folder id from openfarm_search_library")] Guid folderId,
                [Description("New name")] string name,
                CancellationToken ct = default) =>
            {
                library = Library(library);
                await request.RequireAsync(library == "parts" ? "parts:list" : "assemblies:view");
                var folders = request.Service<ILibraryFolderService>();
                var folder = await request.WriteAsync(new RenameFolderRequest { Library = library, Id = folderId, Name = name },
                    () => folders.RenameAsync(library, folderId, name, ct), ct);
                return new McpFolder(folder.Id, library, Path(Paths(await folders.ListAllAsync(library, ct)), folder.Id));
            },
            "library:manage"),
        McpTools.Write("openfarm_move_library_items", "Move library items",
            "Moves parts or assemblies, and folders with everything in them, into one folder or the root. Folders move one by one, then all "
            + "items together (all items or none). On an error, read the folder with openfarm_search_library: earlier folders may have moved.",
            destructive: false, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description(LibraryArg)] string library,
                [Description("Destination folder id; omit for the root")] Guid? toFolderId = null,
                [Description("Part or assembly ids to move, up to 200")] Guid[]? itemIds = null,
                [Description("Folder ids to move")] Guid[]? folderIds = null,
                CancellationToken ct = default) =>
            {
                library = Library(library);
                await request.RequireAsync(library == "parts" ? "parts:list" : "assemblies:view");
                if (itemIds is not { Length: > 0 } && folderIds is not { Length: > 0 }) throw new ArgumentException("Pass itemIds, folderIds or both.");
                var folders = request.Service<ILibraryFolderService>();
                itemIds = itemIds?.Distinct().ToArray();
                foreach (var id in (folderIds ?? []).Distinct())
                    await request.WriteAsync(new MoveFolderRequest { Library = library, Id = id, ParentId = toFolderId },
                        () => folders.MoveFolderAsync(library, id, toFolderId, ct), ct);
                if (itemIds is { Length: > 0 })
                {
                    var dto = new MoveLibraryItemsRequest { Library = library, Ids = [.. itemIds], FolderId = toFolderId };
                    await request.WriteAsync(dto, async () =>
                    {
                        await folders.MoveItemsAsync(library, dto.Ids, toFolderId, ct);
                        return new MovedItems(dto.Ids);
                    }, ct);
                }
                return new McpFolder(toFolderId ?? Guid.Empty, library, Path(Paths(await folders.ListAllAsync(library, ct)), toFolderId));
            },
            "library:manage"),
        McpTools.Write("openfarm_update_parts", "Update parts",
            "Renames parts and adds or removes their tags, one entry per part. Each part saves on its own.",
            destructive: false, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("1–100 parts to change")] McpPartUpdate[] updates,
                CancellationToken ct = default) =>
            {
                if (updates is not { Length: > 0 and <= 100 }) throw new ArgumentException("Pass 1–100 updates.");
                var handler = request.Service<ICommandHandler<EditPartCommand, Part>>();
                var saved = new List<McpLibraryItem>();
                foreach (var update in updates)
                {
                    var part = await PartAsync(request, update.PartId, ct);
                    var tags = part.Tags.Concat(update.AddTags ?? [])
                        .Where(t => !(update.RemoveTags ?? []).Contains(t, StringComparer.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var dto = new EditPartRequest
                    {
                        Id = part.Id, Name = update.Name ?? part.Name, Tags = tags, UnitsPerPrint = part.UnitsPerPrint,
                        FilamentRequirements = [.. part.FilamentRequirements.Select(r => new EditPartFilamentRequirementRequest
                        {
                            RequirementId = r.Id, NarrowFilamentVariantId = r.NarrowFilamentVariantId, NarrowFilamentProfileId = r.NarrowFilamentProfileId,
                            NarrowFilamentId = r.NarrowFilamentId,
                            AnyColor = r.AnyColor,
                        })],
                    };
                    await request.WriteAsync(dto, async () =>
                    {
                        await handler.HandleAsync(new EditPartCommand(dto.Id, dto.Name, dto.Tags, dto.UnitsPerPrint,
                            dto.FilamentRequirements.Select(r => new PartFilamentRequirementNarrowingInput(r.RequirementId, r.NarrowFilamentVariantId, r.NarrowFilamentProfileId, r.NarrowFilamentId, r.AnyColor)).ToList()), ct);
                        return PartResponse.FromDetail(await PartAsync(request, dto.Id, ct));
                    }, ct);
                    saved.Add(new McpLibraryItem("part", dto.Id, dto.Name, "", dto.Tags));
                }
                return new McpUpdatedParts(saved);
            },
            "parts:edit"),
        McpTools.Write("openfarm_save_assembly", "Save assembly",
            "Creates an assembly when assemblyId is omitted (name and lines required), or changes one. On a change, lines merge by kind and id: "
            + "a listed line sets that quantity, quantity 0 drops it from the bill of materials, and unlisted lines stay. "
            + "pricingMode and pricingValue set the selling-price rule used with fee profiles.",
            destructive: false, idempotent: false, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Assembly to change; omit to create one")] Guid? assemblyId = null,
                [Description("Name; required to create")] string? name = null,
                [Description("Bill of materials lines")] McpBomLine[]? lines = null,
                [Description("Create only: folder id; omit for the root. Move later with openfarm_move_library_items")] Guid? folderId = null,
                [Description("Price (a fixed amount in the farm currency), Margin (percent of price kept after fees) or Markup (percent on cost)")] string? pricingMode = null,
                [Description("The amount or percent for pricingMode")] decimal? pricingValue = null,
                CancellationToken ct = default) =>
            {
                AssemblyPricingDto? pricing = pricingMode is null && pricingValue is null ? null
                    : pricingMode is not null && pricingValue is not null ? new AssemblyPricingDto { Mode = pricingMode, Value = pricingValue.Value }
                    : throw new ArgumentException("Pass pricingMode and pricingValue together.");
                if (assemblyId is null)
                {
                    var create = new CreateAssemblyRequest
                    {
                        Name = name ?? "", FolderId = folderId, Pricing = pricing,
                        Lines = [.. (lines ?? []).Select(l => new CreateAssemblyLineRequest { TargetKind = Kind(l.Kind), TargetId = l.Id, Quantity = l.Quantity })],
                    };
                    var created = await request.WriteAsync(create, async () =>
                    {
                        await request.Service<ILibraryFolderService>().ValidateDestinationAsync("assemblies", folderId, ct);
                        var assembly = await request.Service<ICommandHandler<CreateAssemblyCommand, Assembly>>().HandleAsync(new CreateAssemblyCommand(
                            create.Name, null, create.Lines.Select(l => new AssemblyLineInput(Enum.Parse<BomLineTargetKind>(l.TargetKind), l.TargetId, l.Quantity)).ToList(),
                            null, pricing?.ToDomain(), folderId), ct);
                        return AssemblyResponse.FromDetail(await AssemblyAsync(request, assembly.Id, ct));
                    }, ct);
                    return await DescribeAsync(request, created.Id, ct);
                }

                var current = await AssemblyAsync(request, assemblyId.Value, ct);
                var merged = current.Lines.Select(l => (LineId: (Guid?)l.Id, Kind: l.TargetKind.ToString(), l.TargetId, l.Quantity)).ToList();
                foreach (var line in lines ?? [])
                {
                    var kind = Kind(line.Kind);
                    var index = merged.FindIndex(m => m.Kind == kind && m.TargetId == line.Id);
                    if (index >= 0 && line.Quantity == 0) merged.RemoveAt(index);
                    else if (index >= 0) merged[index] = merged[index] with { Quantity = line.Quantity };
                    else if (line.Quantity != 0) merged.Add((null, kind, line.Id, line.Quantity));
                }
                var edit = new EditAssemblyRequest
                {
                    Id = current.Id, Name = name ?? current.Name, Description = current.Description,
                    Lines = lines is null ? null : [.. merged.Select(m => new EditAssemblyLineRequest { LineId = m.LineId, TargetKind = m.Kind, TargetId = m.TargetId, Quantity = m.Quantity })],
                };
                if (pricing is not null) edit.Pricing = pricing;
                await request.WriteAsync(edit, async () =>
                {
                    await request.Service<ICommandHandler<EditAssemblyCommand, Assembly>>().HandleAsync(new EditAssemblyCommand(edit.Id, edit.Name, edit.Description,
                        edit.Lines?.Select(l => new AssemblyLineEditInput(l.LineId, Enum.Parse<BomLineTargetKind>(l.TargetKind), l.TargetId, l.Quantity)).ToList(),
                        null, edit.Pricing?.ToDomain(), edit.HasPricing), ct);
                    return AssemblyResponse.FromDetail(await AssemblyAsync(request, edit.Id, ct));
                }, ct);
                return await DescribeAsync(request, edit.Id, ct);
            },
            "assemblies:create", "assemblies:edit"),
    ];

    /// <summary>The activity row for a move names the moved items.</summary>
    private sealed record MovedItems(IReadOnlyList<Guid> Ids) : OpenFarm.Api.Processors.IHasEntityIds
    {
        public IReadOnlyList<string> EntityIds => [.. Ids.Select(id => id.ToString())];
    }

    private static string Library(string? library) => library?.ToLowerInvariant() switch
    {
        "parts" or "part" => "parts",
        "assemblies" or "assembly" => "assemblies",
        _ => throw new ArgumentException("library must be parts or assemblies."),
    };

    private static string Kind(string kind) => Enum.TryParse<BomLineTargetKind>(kind, ignoreCase: true, out var k) && Enum.IsDefined(k)
        ? k.ToString() : throw new ArgumentException("kind must be Part, Assembly or NonPrintedComponent.");

    private static async Task<PartDetail> PartAsync(RequestContext<CallToolRequestParams> request, Guid partId, CancellationToken ct) =>
        McpTools.Found(await request.Service<IQueryHandler<GetPartQuery, PartDetail?>>().HandleAsync(new GetPartQuery(partId), ct),
            "Part", "Use openfarm_search_library to find part ids.");

    private static async Task<AssemblyDetail> AssemblyAsync(RequestContext<CallToolRequestParams> request, Guid assemblyId, CancellationToken ct) =>
        McpTools.Found(await request.Service<IQueryHandler<GetAssemblyQuery, AssemblyDetail?>>().HandleAsync(new GetAssemblyQuery(assemblyId), ct),
            "Assembly", "Use openfarm_search_library with library assemblies to find assembly ids.");

    private static async Task<McpAssembly> DescribeAsync(RequestContext<CallToolRequestParams> request, Guid assemblyId, CancellationToken ct)
    {
        var assembly = await AssemblyAsync(request, assemblyId, ct);
        var paths = Paths(await request.Service<ILibraryFolderService>().ListAllAsync("assemblies", ct));
        return new McpAssembly(assembly.Id, assembly.Name, assembly.Description, Path(paths, assembly.FolderId),
            assembly.Lines.Select(l => new McpAssemblyLine(l.TargetKind.ToString(), l.TargetId, l.TargetName, l.Quantity)).ToList(),
            AssemblyPricingDto.From(assembly.Pricing), null, null);
    }

    private sealed record FolderPath(string Name, Guid? ParentId, string Path);

    private static Dictionary<Guid, FolderPath> Paths(IReadOnlyList<LibraryFolder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);
        string PathOf(LibraryFolder f, int depth) =>
            depth < 32 && f.ParentId is { } parent && byId.TryGetValue(parent, out var p) ? $"{PathOf(p, depth + 1)}/{f.Name}" : f.Name;
        return folders.ToDictionary(f => f.Id, f => new FolderPath(f.Name, f.ParentId, PathOf(f, 0)));
    }

    private static string Path(IReadOnlyDictionary<Guid, FolderPath> paths, Guid? folderId) =>
        folderId is { } id && paths.TryGetValue(id, out var f) ? f.Path : "";

    private static string Parent(string path) => path.Contains('/', StringComparison.Ordinal) ? path[..path.LastIndexOf('/')] : "";

    private static bool LikeAll(string name, string? query) =>
        !string.IsNullOrWhiteSpace(query)
        && query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
}
