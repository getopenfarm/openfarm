using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IExternalPrintRepository
{
    Task AddAsync(ExternalPrint print, CancellationToken cancellationToken);

    /// <summary>The printer's print that has started but not yet ended, with its filaments loaded.</summary>
    Task<ExternalPrint?> GetOpenByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken);

    Task<ExternalPrint?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Newest first. <paramref name="status"/> is "Active", a status name, or null for everything except Cancelled; the counts
    /// cover the search but not the status filter, like the queue list's.
    /// </summary>
    Task<(IReadOnlyList<ExternalPrint> Prints, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts)> ListAsync(
        int page, int pageSize, string? status, string? search, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
