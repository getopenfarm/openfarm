namespace OpenFarm.Application.FilamentTracking;

/// <summary>Read side of the spool inventory, kept apart from <see cref="ISpoolRepository"/> (the aggregate's write-side persistence).</summary>
public interface ISpoolReadModel
{
    /// <summary>
    /// The joined spool read behind <see cref="GetSpoolsQuery"/>: filters, search and the catalog/vendor/slot joins all run
    /// in one SQL statement, ordered by creation time, so only the requested page of matching rows leaves the database.
    /// </summary>
    Task<SpoolRowPage> ListRowsAsync(GetSpoolsQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Weight history, one spool's or every spool's, newest first, each entry joined to its spool's color and to the print it came
    /// from (left join: a manual entry's print id is unvalidated and a print may be gone). Null when the one spool does not exist.
    /// </summary>
    Task<SpoolHistoryPage?> ListHistoryAsync(GetSpoolHistoryQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Every colour's stock: its unfinished spools valued through <see cref="Domain.FilamentTracking.FilamentPricing"/>, the
    /// grams prints and manual usage took since <paramref name="usedSince"/>, and the low-stock threshold that applies.
    /// </summary>
    Task<IReadOnlyList<VariantStock>> ListStockAsync(DateTimeOffset usedSince, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="MaterialsReport"/> over the query's range: the lines of prints that closed in it once deduction ran on them (a
    /// print cancelled before it started has none), and the write-offs and restores recorded in it.
    /// </summary>
    Task<MaterialsReport> GetMaterialsReportAsync(GetMaterialsReportQuery query, CancellationToken cancellationToken);
}
