using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.Assemblies;

/// <summary>FR-009 — pure, side-effect-free. Validates quantity (FR-009e) before delegating to the recursive CTE (research.md R1).</summary>
public sealed class ExplodeAssemblyQueryHandler(IAssemblyGraphQueryService graphQueryService, IAssemblyRepository assemblyRepository)
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

        return await graphQueryService.ExplodeAsync(Organization.WellKnownId, query.AssemblyId, query.Quantity, cancellationToken);
    }
}
