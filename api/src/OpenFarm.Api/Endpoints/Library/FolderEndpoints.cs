using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Library;
using OpenFarm.Domain.Library;

namespace OpenFarm.Api.Endpoints.Library;

public class FolderLibraryRequest { public string Library { get; set; } = string.Empty; }
public sealed class ListFoldersRequest : FolderLibraryRequest
{
    public Guid? ParentId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
public sealed class CreateFolderRequest : FolderLibraryRequest, IActivityLoggable
{
    public string EntityType => "LibraryFolder";
    public string EventType => "LibraryFolderCreated";
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}
public class FolderIdRequest : FolderLibraryRequest, IActivityLoggable
{
    public Guid Id { get; set; }
    public string EntityType => "LibraryFolder";
    public virtual string EventType => "LibraryFolderRemoved";
}
public sealed class RenameFolderRequest : FolderIdRequest
{
    public string Name { get; set; } = string.Empty;
    public override string EventType => "LibraryFolderRenamed";
}
public sealed class MoveFolderRequest : FolderIdRequest
{
    public Guid? ParentId { get; set; }
    public override string EventType => "LibraryFolderMoved";
}
public sealed class MoveLibraryItemsRequest : FolderLibraryRequest, IActivityLoggable
{
    public string EntityType => Library == "parts" ? "Part" : "Assembly";
    public string EventType => "LibraryItemsMoved";
    public List<Guid> Ids { get; set; } = [];
    public Guid? FolderId { get; set; }
}
[Permission("library:view")]
public sealed class ListFoldersEndpoint(ILibraryFolderService service) : Endpoint<ListFoldersRequest, FolderPage>
{
    public override void Configure() { Get("/api/v1/library/{library}/folders"); Policies("Permission"); }
    public override async Task HandleAsync(ListFoldersRequest req, CancellationToken ct) =>
        Response = await service.ListAsync(req.Library, req.ParentId, Math.Max(1, req.Page), req.PageSize is < 1 or > 200 ? 25 : req.PageSize, ct);
}
[Permission("library:manage")]
public sealed class CreateFolderEndpoint(ILibraryFolderService service) : Endpoint<CreateFolderRequest, LibraryFolder>
{
    public override void Configure() { Post("/api/v1/library/{library}/folders"); Policies("Permission"); }
    public override async Task HandleAsync(CreateFolderRequest req, CancellationToken ct)
    { Response = await service.CreateAsync(req.Library, req.Name, req.ParentId, ct); HttpContext.Response.StatusCode = 201; }
}
[Permission("library:manage")]
public sealed class RenameFolderEndpoint(ILibraryFolderService service) : Endpoint<RenameFolderRequest, LibraryFolder>
{
    public override void Configure() { Patch("/api/v1/library/{library}/folders/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(RenameFolderRequest req, CancellationToken ct) =>
        Response = await service.RenameAsync(req.Library, req.Id, req.Name, ct);
}
[Permission("library:manage")]
public sealed class MoveFolderEndpoint(ILibraryFolderService service) : Endpoint<MoveFolderRequest, LibraryFolder>
{
    public override void Configure() { Post("/api/v1/library/{library}/folders/{id}/move"); Policies("Permission"); }
    public override async Task HandleAsync(MoveFolderRequest req, CancellationToken ct) =>
        Response = await service.MoveFolderAsync(req.Library, req.Id, req.ParentId, ct);
}
[Permission("library:delete")]
public sealed class RemoveFolderEndpoint(ILibraryFolderService service) : Endpoint<FolderIdRequest>
{
    public override void Configure() { Delete("/api/v1/library/{library}/folders/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(FolderIdRequest req, CancellationToken ct)
    { await service.RemoveAsync(req.Library, req.Id, ct); await Send.NoContentAsync(ct); }
}
[Permission("library:manage")]
public sealed class MoveLibraryItemsEndpoint(ILibraryFolderService service) : Endpoint<MoveLibraryItemsRequest>
{
    public override void Configure() { Post("/api/v1/library/{library}/items/move"); Policies("Permission"); }
    public override async Task HandleAsync(MoveLibraryItemsRequest req, CancellationToken ct)
    { await service.MoveItemsAsync(req.Library, req.Ids, req.FolderId, ct); await Send.NoContentAsync(ct); }
}
