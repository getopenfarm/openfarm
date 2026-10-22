namespace OpenFarm.Application.FilamentTracking;

/// <param name="Quantity">How many identical spools to add in one step, 1 to <see cref="MaxQuantity"/>; the label, weights and purchase info apply to each.</param>
/// <param name="Price">What was paid for each whole spool; null when unknown.</param>
public sealed record RegisterSpoolCommand(
    Guid FilamentVariantId,
    string? Label,
    decimal SpoolWeightGrams,
    decimal? RemainingWeightGrams,
    int Quantity = 1,
    decimal? Price = null,
    string? Supplier = null,
    string? OrderNumber = null,
    DateOnly? ReceivedOn = null)
{
    public const int MaxQuantity = 50;
}
