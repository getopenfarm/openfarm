using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed class CostCatalogService(INonPrintedComponentRepository items, IAssemblyRepository assemblies)
{
    public Task<NonPrintedComponent?> GetAsync(Guid id, CancellationToken ct) => items.GetByIdAsync(id, ct);

    public Task<NonPrintedComponent> SaveAsync(Guid? id, string name, string? icon, CatalogPricing pricing, CancellationToken ct) =>
        assemblies.RunWithStructuralLockAsync(async token =>
        {
            pricing.Validate();
            var item = id is { } existingId
                ? await items.GetByIdAsync(existingId, token) ?? throw new NonPrintedComponentNotFoundException(existingId)
                : NonPrintedComponent.Create(name, null);
            if (id.HasValue && (item.ItemType != pricing.ItemType || item.Unit != pricing.Unit))
            {
                var references = await assemblies.GetReferencingAssemblyNamesAsync(BomLineTargetKind.NonPrintedComponent, item.Id, token);
                if (references.Count > 0)
                    throw new ArgumentException("Unit cannot change while an assembly references this item.");
            }
            item.Edit(name, pricing.DirectRate);
            item.Configure(pricing);
            item.SetIcon(icon);
            if (id.HasValue) await items.SaveChangesAsync(token);
            else await items.AddAsync(item, token);
            return item;
        }, ct);

    public Task<bool> RemoveAsync(Guid id, CancellationToken ct) => assemblies.RunWithStructuralLockAsync(async token =>
    {
        var item = await items.GetByIdAsync(id, token) ?? throw new NonPrintedComponentNotFoundException(id);
        var references = await assemblies.GetReferencingAssemblyNamesAsync(BomLineTargetKind.NonPrintedComponent, id, token);
        if (references.Count > 0) throw new NonPrintedComponentReferencedException(id, references);
        await items.RemoveAsync(item, token);
        return true;
    }, ct);
}
