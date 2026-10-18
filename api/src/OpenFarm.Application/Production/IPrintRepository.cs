using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>Reads and saves <see cref="Print"/> across both kinds (dispatched jobs and prints started on the printer).</summary>
public interface IPrintRepository
{
    /// <summary>The print with its filament lines loaded, whichever kind it is.</summary>
    Task<Print?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Closed, undismissed prints with at least one filament line automatic deduction left unrecorded, newest end first,
    /// with their lines loaded. <paramref name="limit"/> caps the page; the total covers all of them.
    /// </summary>
    Task<(IReadOnlyList<Print> Prints, int TotalCount)> ListNeedingFilamentFollowUpAsync(int limit, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
