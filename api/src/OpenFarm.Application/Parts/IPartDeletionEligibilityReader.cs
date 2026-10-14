using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface IPartDeletionEligibilityReader
{
    Task<IReadOnlyList<PartDeletionEligibility>> CheckAsync(IReadOnlyList<Guid> partIds, CancellationToken cancellationToken);
}
