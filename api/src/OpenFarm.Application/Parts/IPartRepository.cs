using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface IPartRepository
{
    Task AddAsync(Part part, CancellationToken cancellationToken);

    Task<Part?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary><paramref name="tags"/> filters to parts carrying ALL given tag names (AND-semantics, FR of US5).</summary>
    Task<(IReadOnlyList<Part> Parts, int TotalCount)> ListAsync(
        IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken);

    Task RemoveAsync(Part part, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
