namespace OpenFarm.Application.FilamentTracking;

/// <summary>Read side of the spool inventory, kept apart from <see cref="ISpoolRepository"/> (the aggregate's write-side persistence).</summary>
public interface ISpoolReadModel
{
    /// <summary>
    /// The joined spool read behind <see cref="GetSpoolsQuery"/>: filters, search and the catalog/vendor/slot joins all run
    /// in one SQL statement, ordered by creation time, so only the requested page of matching rows leaves the database.
    /// </summary>
    Task<SpoolRowPage> ListRowsAsync(GetSpoolsQuery query, CancellationToken cancellationToken);
}
