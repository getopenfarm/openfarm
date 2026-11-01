using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Library;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Library;

public sealed class AssemblyPreviewReader(OpenFarmDbContext db) : IAssemblyPreviewReader
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ReadAsync(IReadOnlyList<Guid> assemblyIds, CancellationToken ct)
    {
        var edges = new Dictionary<Guid, List<(Guid? Part, Guid? Assembly)>>();
        var frontier = assemblyIds.Distinct().ToList();
        // Query each graph level for the whole page, never once per card.
        while (frontier.Count > 0)
        {
            var lines = await db.BomLines.AsNoTracking().Where(l => frontier.Contains(l.AssemblyId))
                .Select(l => new { l.AssemblyId, l.TargetPartId, l.TargetAssemblyId }).ToListAsync(ct);
            foreach (var id in frontier) edges.TryAdd(id, []);
            foreach (var line in lines) edges[line.AssemblyId].Add((line.TargetPartId, line.TargetAssemblyId));
            frontier = lines.Where(l => l.TargetAssemblyId.HasValue).Select(l => l.TargetAssemblyId!.Value)
                .Distinct().Where(id => !edges.ContainsKey(id)).ToList();
        }
        var partIds = edges.Values.SelectMany(e => e).Where(e => e.Part.HasValue).Select(e => e.Part!.Value).Distinct().ToList();
        var thumbnails = await db.PartThumbnails.AsNoTracking().Where(t => partIds.Contains(t.PartId))
            .OrderBy(t => t.PlateIndex).Select(t => new { t.PartId, t.Bytes }).ToListAsync(ct);
        var images = thumbnails.GroupBy(t => t.PartId).ToDictionary(g => g.Key, g => Convert.ToBase64String(g.First().Bytes));
        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var root in assemblyIds)
        {
            var seen = new HashSet<Guid>();
            var parts = new SortedSet<Guid>();
            void Walk(Guid id)
            {
                if (!seen.Add(id) || !edges.TryGetValue(id, out var children)) return;
                foreach (var child in children)
                {
                    if (child.Part.HasValue) parts.Add(child.Part.Value);
                    if (child.Assembly.HasValue) Walk(child.Assembly.Value);
                }
            }
            Walk(root);
            result[root] = parts.Where(images.ContainsKey).Select(id => images[id]).Take(4).ToList();
        }
        return result;
    }
}
