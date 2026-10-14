using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface ITagRepository
{
    Task AddAsync(Tag tag, CancellationToken cancellationToken);

    /// <summary>Tracks a new tag as Added without saving, so it commits with the caller's own SaveChanges.</summary>
    void Stage(Tag tag);

    Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
