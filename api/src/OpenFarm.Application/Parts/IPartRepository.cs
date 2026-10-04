using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface IPartRepository
{
    Task AddAsync(Part part, CancellationToken cancellationToken);

    Task<Part?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary><paramref name="tags"/> filters to parts carrying ALL given tag names (AND-semantics, FR of US5).</summary>
    Task<(IReadOnlyList<Part> Parts, int TotalCount)> ListAsync(
        IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Confirmed parts whose estimate is still <c>Resolving</c> — only reachable at process startup
    /// (see <see cref="IPartProcessingJobRepository.ListOrphanedInFlightAsync"/>'s remarks); a
    /// previous process lifetime ended before its trailing estimate continuation could complete.
    /// </summary>
    Task<IReadOnlyList<Part>> ListWithResolvingEstimateAsync(CancellationToken cancellationToken);

    Task RemoveAsync(Part part, CancellationToken cancellationToken);

    void TrackReplacementChildren(Part part) { }

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
