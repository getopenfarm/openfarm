using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed record AssemblyCostContribution(Guid CatalogItemId, string Name, string Category, string Unit,
    decimal Quantity, decimal? Rate, decimal? Cost, string Purpose);

public sealed class AssemblyCostRollup(IAssemblyRepository assemblies, INonPrintedComponentRepository items)
{
    public async Task<(IReadOnlyList<AssemblyCostContribution> Production, IReadOnlyList<AssemblyCostContribution> FinalProduct)> CalculateAsync(
        Guid rootId, decimal quantity, CancellationToken ct)
    {
        var production = new List<AssemblyCostContribution>();
        var final = new List<AssemblyCostContribution>();
        var cache = new Dictionary<Guid, Assembly>();
        var itemCache = new Dictionary<Guid, NonPrintedComponent>();
        async Task Visit(Guid id, decimal multiplier, int depth)
        {
            if (depth > 10) throw new ArgumentException("Assembly nesting exceeds the depth limit.");
            if (!cache.TryGetValue(id, out var assembly))
            {
                assembly = await assemblies.GetByIdAsync(id, ct) ?? throw new AssemblyNotFoundException(id);
                cache[id] = assembly;
            }
            foreach (var line in assembly.AdditionalCosts)
            {
                if (line.Purpose == "FinalProduct" && depth != 1) continue;
                if (!itemCache.TryGetValue(line.CatalogItemId, out var item))
                {
                    item = await items.GetByIdAsync(line.CatalogItemId, ct) ?? throw new NonPrintedComponentNotFoundException(line.CatalogItemId);
                    itemCache[item.Id] = item;
                }
                var usage = line.Input.Usage(item) * multiplier;
                var row = new AssemblyCostContribution(item.Id, item.Name, item.Category, item.Unit, usage, item.UnitCost, usage * item.UnitCost, line.Purpose);
                (line.Purpose == "Production" ? production : final).Add(row);
            }
            foreach (var line in assembly.Lines.Where(l => l.TargetKind == BomLineTargetKind.Assembly))
                await Visit(line.TargetAssemblyId!.Value, multiplier * line.Quantity, depth + 1);
        }
        await Visit(rootId, quantity, 1);
        return (production, final);
    }
}
