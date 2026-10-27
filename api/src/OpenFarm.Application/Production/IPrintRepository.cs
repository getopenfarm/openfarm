using OpenFarm.Domain.Printers;
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

    /// <summary>
    /// What each printer's current print is called, by printer id: the part name of an OpenFarm job (its open print, or while
    /// awaiting clear the one that just ended), else the printer's own job name.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetCurrentPrintNamesAsync(IReadOnlyCollection<Printer> printers, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
