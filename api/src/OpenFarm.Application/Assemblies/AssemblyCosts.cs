using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public static class AssemblyCosts
{
    public static async Task ValidateAsync(IReadOnlyList<AssemblyCostInput>? inputs, INonPrintedComponentRepository repository, CancellationToken ct)
    {
        foreach (var input in inputs ?? [])
        {
            var item = await repository.GetByIdAsync(input.CatalogItemId, ct)
                ?? throw new NonPrintedComponentNotFoundException(input.CatalogItemId);
            input.Usage(item);
        }
    }
}
