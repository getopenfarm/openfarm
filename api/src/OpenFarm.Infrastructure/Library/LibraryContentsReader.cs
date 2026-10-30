using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Library;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Library;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311 // LOWER is translated to SQL and follows database collation.
namespace OpenFarm.Infrastructure.Library;

public sealed class LibraryContentsReader(OpenFarmDbContext db, ILibraryFolderService folders) : ILibraryContentsReader
{
    public async Task<LibraryContentsPage> ReadAsync(string library, Guid? parentId, int page, int pageSize, ListSort? sorting, CancellationToken ct)
    {
        await folders.ValidateDestinationAsync(library, parentId, ct);
        var ancestors = new List<LibraryFolder>();
        for (var cursor = parentId; cursor.HasValue;)
        {
            var folder = await db.LibraryFolders.AsNoTracking().SingleOrDefaultAsync(f => f.Id == cursor.Value && f.Library == library, ct)
                ?? throw new LibraryFolderNotFoundException();
            ancestors.Add(folder);
            cursor = folder.ParentId;
        }
        ancestors.Reverse();
        var folderKeys = db.LibraryFolders.Where(f => f.Library == library && f.ParentId == parentId)
            .Select(f => new EntryKey { Id = f.Id, Name = f.Name, CreatedAtUtc = f.CreatedAtUtc, Rank = 0 });
        var itemKeys = library == "parts"
            ? db.Parts.Where(p => p.FolderId == parentId).Select(p => new EntryKey { Id = p.Id, Name = p.Name, CreatedAtUtc = p.CreatedAtUtc, Rank = 1 })
            : db.Assemblies.Where(a => a.FolderId == parentId).Select(a => new EntryKey { Id = a.Id, Name = a.Name, CreatedAtUtc = a.CreatedAtUtc, Rank = 1 });
        var keys = folderKeys.Concat(itemKeys);
        var count = await keys.CountAsync(ct);
        var ordered = keys.OrderBy(k => k.Rank);
        ordered = sorting is null ? ordered.ThenByDescending(k => k.CreatedAtUtc)
            : sorting.Descending ? ordered.ThenByDescending(k => k.Name.ToLower()) : ordered.ThenBy(k => k.Name.ToLower());
        var pageKeys = await ordered.ThenBy(k => k.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var folderIds = pageKeys.Where(k => k.Rank == 0).Select(k => k.Id).ToList();
        var itemIds = pageKeys.Where(k => k.Rank == 1).Select(k => k.Id).ToList();
        var pageFolders = await db.LibraryFolders.AsNoTracking().Where(f => folderIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var parts = library == "parts" && itemIds.Count > 0
            ? (await db.Parts.AsNoTracking().Where(p => itemIds.Contains(p.Id)).Include(p => p.FilamentRequirements).Include(p => p.Thumbnails).Include(p => p.Tags).AsSplitQuery().ToListAsync(ct))
                .ToDictionary(p => p.Id, GetPartsQueryHandler.FromPart)
            : [];
        var assemblies = library == "assemblies" && itemIds.Count > 0
            ? await db.Assemblies.AsNoTracking().Where(a => itemIds.Contains(a.Id))
                .Select(a => new AssemblySummary(a.Id, a.Name, a.Description, a.Lines.Count)).ToDictionaryAsync(a => a.Id, ct)
            : [];
        var entries = new List<LibraryContentsEntry>();
        foreach (var key in pageKeys)
        {
            // A concurrent removal can disappear between the key and detail reads. Omit it;
            // the next settled read supplies the authoritative count and page recovery.
            if (key.Rank == 0 && pageFolders.TryGetValue(key.Id, out var folder)) entries.Add(new(key.Id, key.Name, "folder", Folder: folder));
            else if (key.Rank == 1 && parts.TryGetValue(key.Id, out var part)) entries.Add(new(key.Id, key.Name, "part", Part: part));
            else if (key.Rank == 1 && assemblies.TryGetValue(key.Id, out var assembly)) entries.Add(new(key.Id, key.Name, "assembly", Assembly: assembly));
        }
        return new(entries, count, ancestors);
    }
    private sealed class EntryKey
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public int Rank { get; init; }
    }
}
