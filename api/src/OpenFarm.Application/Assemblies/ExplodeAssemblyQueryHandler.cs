using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary>FR-009 — pure, side-effect-free. Validates quantity (FR-009e) before delegating to the recursive CTE (research.md R1).</summary>
public sealed class ExplodeAssemblyQueryHandler(IAssemblyGraphQueryService graphQueryService, IAssemblyRepository assemblyRepository, INonPrintedComponentRepository items)
    : IQueryHandler<ExplodeAssemblyQuery, BomExplosionResult>
{
    public async Task<BomExplosionResult> HandleAsync(ExplodeAssemblyQuery query, CancellationToken cancellationToken)
    {
        if (query.Quantity < 1)
        {
            throw new InvalidExplosionQuantityException(query.Quantity);
        }

        if (!await assemblyRepository.ExistsAsync(query.AssemblyId, cancellationToken))
        {
            throw new AssemblyNotFoundException(query.AssemblyId);
        }

        var explosion = await graphQueryService.ExplodeAsync(query.AssemblyId, query.Quantity, cancellationToken);
        var costs = await new AssemblyCostRollup(assemblyRepository, items).CalculateAsync(query.AssemblyId, query.Quantity, cancellationToken);
        return explosion with { ProductionCosts = costs.Production, FinalProductCosts = costs.FinalProduct };
    }
}
