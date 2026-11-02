using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Library;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Library;

public sealed class LibraryFolderPreviewReader(OpenFarmDbContext db, IAssemblyPreviewReader assemblyPreviews) : ILibraryFolderPreviewReader
{
    private const int MaxImages = 4;
    // ponytail: assemblies without part thumbnails can crowd out ones with images; raise if folders show icons despite images deeper down.
    private const int MaxAssembliesPerFolder = 16;

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ReadAsync(string library, IReadOnlyList<Guid> folderIds, CancellationToken ct)
    {
        var result = folderIds.Distinct().ToDictionary(id => id, _ => (IReadOnlyList<string>)[]);
        if (result.Count == 0) return result;
        // Map every descendant folder to its page root, one query per tree level for the whole page.
        var rootOf = result.Keys.ToDictionary(id => id, id => id);
        var frontier = result.Keys.ToList();
        while (frontier.Count > 0)
        {
            var children = await db.LibraryFolders.AsNoTracking().Where(f => f.Library == library && f.ParentId.HasValue && frontier.Contains(f.ParentId.Value))
                .Select(f => new { f.Id, ParentId = f.ParentId!.Value }).ToListAsync(ct);
            frontier = [];
            foreach (var child in children.Where(c => rootOf.TryAdd(c.Id, rootOf[c.ParentId]))) frontier.Add(child.Id);
        }
        var subtree = rootOf.Keys.Select(id => (Guid?)id).ToList();
        if (library == "parts")
        {
            var parts = await db.Parts.AsNoTracking().Where(p => subtree.Contains(p.FolderId) && p.Thumbnails.Any())
                .OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p => new { p.Id, FolderId = p.FolderId!.Value }).ToListAsync(ct);
            var picked = parts.GroupBy(p => rootOf[p.FolderId]).ToDictionary(g => g.Key, g => g.Take(MaxImages).Select(p => p.Id).ToList());
            var partIds = picked.Values.SelectMany(ids => ids).ToList();
            var thumbnails = await db.PartThumbnails.AsNoTracking().Where(t => partIds.Contains(t.PartId))
                .OrderBy(t => t.PlateIndex).Select(t => new { t.PartId, t.Bytes }).ToListAsync(ct);
            var images = thumbnails.GroupBy(t => t.PartId).ToDictionary(g => g.Key, g => Convert.ToBase64String(g.First().Bytes));
            foreach (var (root, ids) in picked) result[root] = ids.Where(images.ContainsKey).Select(id => images[id]).ToList();
            return result;
        }
        var assemblies = await db.Assemblies.AsNoTracking().Where(a => subtree.Contains(a.FolderId))
            .OrderBy(a => a.Name).ThenBy(a => a.Id).Select(a => new { a.Id, FolderId = a.FolderId!.Value }).ToListAsync(ct);
        var candidates = assemblies.GroupBy(a => rootOf[a.FolderId]).ToDictionary(g => g.Key, g => g.Take(MaxAssembliesPerFolder).Select(a => a.Id).ToList());
        var previews = await assemblyPreviews.ReadAsync(candidates.Values.SelectMany(ids => ids).ToList(), ct);
        foreach (var (root, ids) in candidates)
            result[root] = ids.SelectMany(id => previews.GetValueOrDefault(id) ?? []).Distinct().Take(MaxImages).ToList();
        return result;
    }
}
