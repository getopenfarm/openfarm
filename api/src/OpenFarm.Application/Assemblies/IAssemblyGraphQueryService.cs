namespace OpenFarm.Application.Assemblies;

public interface IAssemblyGraphQueryService
{
    /// <summary>FR-009 — pure, side-effect-free BOM explosion (research.md R1), a literal recursive CTE at the database layer.</summary>
    Task<BomExplosionResult> ExplodeAsync(Guid organizationId, Guid rootAssemblyId, int quantity, CancellationToken cancellationToken);

    /// <summary>The organization's flat Assembly-to-Assembly edge list, feeding <see cref="OpenFarm.Domain.Assemblies.AssemblyGraph"/> (research.md R2).</summary>
    Task<IReadOnlyList<(Guid ParentAssemblyId, Guid ChildAssemblyId)>> GetOrganizationEdgesAsync(Guid organizationId, CancellationToken cancellationToken);
}
