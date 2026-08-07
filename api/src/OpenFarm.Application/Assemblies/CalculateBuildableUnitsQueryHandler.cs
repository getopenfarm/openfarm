using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.Assemblies;

/// <summary>
/// FR-010, research.md R6 — calls <see cref="IAssemblyGraphQueryService.ExplodeAsync"/> at quantity 1
/// to get each printed part's aggregated per-unit requirement, then a pure, deterministic
/// floor/min calculation. Non-printed leaves are dropped before the calculation runs (FR-010b).
/// </summary>
public sealed class CalculateBuildableUnitsQueryHandler(IAssemblyGraphQueryService graphQueryService, IAssemblyRepository assemblyRepository)
    : IQueryHandler<CalculateBuildableUnitsQuery, BuildableUnitsResult>
{
    public async Task<BuildableUnitsResult> HandleAsync(CalculateBuildableUnitsQuery query, CancellationToken cancellationToken)
    {
        if (!await assemblyRepository.ExistsAsync(query.AssemblyId, cancellationToken))
        {
            throw new AssemblyNotFoundException(query.AssemblyId);
        }

        var explosion = await graphQueryService.ExplodeAsync(Organization.WellKnownId, query.AssemblyId, quantity: 1, cancellationToken);

        if (explosion.PrintedParts.Count == 0)
        {
            return new BuildableUnitsResult { IsApplicable = false, NotApplicableReason = "Assembly contains no printed parts." };
        }

        var value = explosion.PrintedParts.Min(part =>
        {
            var goodQuantity = query.GoodQuantities.GetValueOrDefault(part.PartId, 0m);
            return (int)Math.Floor(goodQuantity / part.Quantity);
        });

        return new BuildableUnitsResult { IsApplicable = true, Value = value };
    }
}
