using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Library;
using OpenFarm.Domain.Library;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311, CA1862 // SQL case folding is translated by EF and follows the database collation.
namespace OpenFarm.Infrastructure.Library;

public sealed class LibraryFolderService(OpenFarmDbContext db) : ILibraryFolderService
{
    public async Task ValidateDestinationAsync(string library, Guid? id, CancellationToken ct)
    {
        LibraryFolder.ValidateLibrary(library);
        if (id.HasValue && !await db.LibraryFolders.AnyAsync(f => f.Id == id && f.Library == library, ct))
            throw new LibraryFolderNotFoundException();
    }
    private async Task<LibraryFolder> FindAsync(string library, Guid id, CancellationToken ct)
    {
        LibraryFolder.ValidateLibrary(library);
        return await db.LibraryFolders.SingleOrDefaultAsync(f => f.Id == id && f.Library == library, ct)
            ?? throw new LibraryFolderNotFoundException();
    }
    public async Task<FolderPage> ListAsync(string library, Guid? parentId, int page, int pageSize, CancellationToken ct)
    {
        await ValidateDestinationAsync(library, parentId, ct);
        var ancestors = new List<LibraryFolder>();
        var cursor = parentId;
        while (cursor.HasValue)
        {
            var folder = await FindAsync(library, cursor.Value, ct);
            ancestors.Add(folder);
            cursor = folder.ParentId;
        }
        ancestors.Reverse();
        var query = db.LibraryFolders.AsNoTracking().Where(f => f.Library == library && f.ParentId == parentId);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(f => f.Name.ToLower()).ThenBy(f => f.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items, count, ancestors);
    }
    // All structural folder writes share this transaction lock. FK restrictions also protect
    // against concurrent item creation, which does not need to take the folder lock.
    private async Task<T> WriteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734062810)", ct);
        try
        {
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw new LibraryConflictException("Library changed. Refresh and try again."); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" or "23505" })
        { throw new LibraryConflictException("Folder contents or name changed. Refresh and try again."); }
    }
    private async Task EnsureUniqueAsync(LibraryFolder folder, CancellationToken ct)
    {
        var name = folder.Name.ToUpperInvariant();
        if (await db.LibraryFolders.AnyAsync(f => f.Library == folder.Library && f.ParentId == folder.ParentId && f.Id != folder.Id && f.Name.ToUpper() == name, ct))
            throw new LibraryConflictException("A folder with that name already exists here.");
    }
    public Task<LibraryFolder> CreateAsync(string library, string name, Guid? parentId, CancellationToken ct) => WriteAsync(async () =>
    {
        await ValidateDestinationAsync(library, parentId, ct);
        var folder = LibraryFolder.Create(library, name, parentId);
        await EnsureUniqueAsync(folder, ct);
        db.LibraryFolders.Add(folder);
        return folder;
    }, ct);
    public Task<LibraryFolder> RenameAsync(string library, Guid id, string name, CancellationToken ct) => WriteAsync(async () =>
    {
        var folder = await FindAsync(library, id, ct);
        folder.Rename(name);
        await EnsureUniqueAsync(folder, ct);
        return folder;
    }, ct);
    public Task<LibraryFolder> MoveFolderAsync(string library, Guid id, Guid? parentId, CancellationToken ct) => WriteAsync(async () =>
    {
        var folder = await FindAsync(library, id, ct);
        await ValidateDestinationAsync(library, parentId, ct);
        var cursor = parentId;
        while (cursor.HasValue)
        {
            if (cursor == id) throw new LibraryConflictException("A folder cannot move into itself or a descendant.");
            cursor = (await FindAsync(library, cursor.Value, ct)).ParentId;
        }
        folder.Move(parentId);
        await EnsureUniqueAsync(folder, ct);
        return folder;
    }, ct);
    public async Task RemoveAsync(string library, Guid id, CancellationToken ct) => await WriteAsync(async () =>
    {
        var folder = await FindAsync(library, id, ct);
        if (await db.LibraryFolders.AnyAsync(f => f.ParentId == id, ct)
            || await db.Parts.AnyAsync(p => p.FolderId == id, ct)
            || await db.Assemblies.AnyAsync(a => a.FolderId == id, ct))
            throw new LibraryConflictException("Move all items and subfolders out before removing this folder.");
        db.LibraryFolders.Remove(folder);
        return true;
    }, ct);
    public async Task MoveItemsAsync(string library, IReadOnlyList<Guid> ids, Guid? folderId, CancellationToken ct) => await WriteAsync(async () =>
    {
        await ValidateDestinationAsync(library, folderId, ct);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is < 1 or > 200 || ids.Distinct().Count() != ids.Count) throw new ArgumentException("Choose 1–200 distinct items.", nameof(ids));
        if (library == "parts")
        {
            var parts = await db.Parts.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            if (parts.Count != ids.Count) throw new LibraryItemNotFoundException();
            foreach (var part in parts) part.MoveToFolder(folderId);
        }
        else
        {
            var assemblies = await db.Assemblies.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
            if (assemblies.Count != ids.Count) throw new LibraryItemNotFoundException();
            foreach (var assembly in assemblies) assembly.MoveToFolder(folderId);
        }
        return true;
    }, ct);
}
