namespace OpenFarm.Application.FilamentTracking;

/// <param name="Quantity">How many identical spools to add in one step, 1 to <see cref="MaxQuantity"/>; the label, weights and purchase info apply to each.</param>
/// <param name="Price">What was paid for each whole spool; null when unknown.</param>
/// <param name="RfidId">The spool's RFID tag, so a printer that reads it loads the spool; only with a quantity of 1.</param>
public sealed record RegisterSpoolCommand(
    Guid FilamentVariantId,
    string? Label,
    decimal SpoolWeightGrams,
    decimal? RemainingWeightGrams,
    int Quantity = 1,
    decimal? Price = null,
    string? Supplier = null,
    string? OrderNumber = null,
    DateOnly? ReceivedOn = null,
    string? RfidId = null)
{
    public const int MaxQuantity = 50;
}
