using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface ITagRepository
{
    Task AddAsync(Tag tag, CancellationToken cancellationToken);

    Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
