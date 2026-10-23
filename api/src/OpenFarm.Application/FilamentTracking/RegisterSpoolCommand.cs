namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// One delivery: its lines of identical spools, and the purchase details they share. Every spool is added in one save, or none.
/// </summary>
/// <param name="RfidId">The spool's RFID tag, so a printer that reads it loads the spool; only for one line of one spool.</param>
public sealed record RegisterSpoolCommand(
    IReadOnlyList<RegisterSpoolLine> Lines,
    string? Supplier = null,
    string? OrderNumber = null,
    DateOnly? ReceivedOn = null,
    string? RfidId = null)
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 500;
    public const int MaxSpools = 1000;
}

/// <param name="Quantity">How many identical spools, 1 to <see cref="RegisterSpoolCommand.MaxQuantity"/>; the line's values apply to each.</param>
/// <param name="RemainingWeightGrams">Grams left on each; null for full.</param>
/// <param name="Price">What was paid for each whole spool; null when unknown.</param>
public sealed record RegisterSpoolLine(
    Guid FilamentVariantId,
    decimal SpoolWeightGrams,
    int Quantity = 1,
    decimal? RemainingWeightGrams = null,
    decimal? Price = null,
    string? Label = null);
