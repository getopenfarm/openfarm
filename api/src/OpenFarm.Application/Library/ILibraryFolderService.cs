using OpenFarm.Domain.Library;

namespace OpenFarm.Application.Library;

public sealed record FolderPage(IReadOnlyList<LibraryFolder> Items, int TotalCount, IReadOnlyList<LibraryFolder> Ancestors);
public interface ILibraryFolderService
{
    Task ValidateDestinationAsync(string library, Guid? id, CancellationToken ct);
    Task<FolderPage> ListAsync(string library, Guid? parentId, int page, int pageSize, CancellationToken ct);
    Task<LibraryFolder> CreateAsync(string library, string name, Guid? parentId, CancellationToken ct);
    Task<LibraryFolder> RenameAsync(string library, Guid id, string name, CancellationToken ct);
    Task<LibraryFolder> MoveFolderAsync(string library, Guid id, Guid? parentId, CancellationToken ct);
    Task RemoveAsync(string library, Guid id, CancellationToken ct);
    Task MoveItemsAsync(string library, IReadOnlyList<Guid> ids, Guid? folderId, CancellationToken ct);
}
