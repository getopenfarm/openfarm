using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IPartRequirementRepository
{
    Task<bool> HasPartReferenceAsync(Guid partId, CancellationToken cancellationToken);

    Task AddRangeAsync(IReadOnlyList<PartRequirement> requirements, CancellationToken cancellationToken);

    Task<PartRequirement?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PartRequirement>> ListByRunLineIdAsync(Guid runLineId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PartRequirement>> ListByProductionRunIdAsync(Guid productionRunId, CancellationToken cancellationToken);
}
