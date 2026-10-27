using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface IPartRepository
{
    Task AddAsync(Part part, CancellationToken cancellationToken);

    /// <summary>Adds the parts one upload was split into in one save, so all are kept or none. The default adds them one at a time, which suits in-memory stand-ins.</summary>
    async Task AddRangeAsync(IReadOnlyList<Part> parts, CancellationToken cancellationToken)
    {
        foreach (var part in parts) await AddAsync(part, cancellationToken);
    }

    Task<Part?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary><paramref name="tags"/> filters to parts carrying ALL given tag names (AND-semantics, FR of US5).</summary>
    Task<(IReadOnlyList<Part> Parts, int TotalCount)> ListAsync(
        IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null);

    /// <summary>
    /// Confirmed parts whose estimate is still <c>Resolving</c> — only reachable at process startup
    /// (see <see cref="IPartProcessingJobRepository.ListOrphanedInFlightAsync"/>'s remarks); a
    /// previous process lifetime ended before its trailing estimate continuation could complete.
    /// </summary>
    Task<IReadOnlyList<Part>> ListWithResolvingEstimateAsync(CancellationToken cancellationToken);

    Task RemoveAsync(Part part, CancellationToken cancellationToken);

    /// <summary>Parts whose current file is <paramref name="revisionHash"/>: the parts one upload was split into, while they share it.</summary>
    Task<IReadOnlyList<Part>> ListByRevisionHashAsync(string revisionHash, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Part>>([]);

    void TrackReplacementChildren(Part part) { }

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
