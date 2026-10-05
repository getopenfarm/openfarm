namespace OpenFarm.Domain.Parts;

public static class PartEstimateAvailability
{
    public static string? MissingMetadataReason(int? minutes, decimal? grams) =>
        minutes is not null && grams is not null ? null : "Some print time or filament weight metadata is unavailable.";

    public static EstimateStatus From(int? minutes, decimal? grams) => (minutes, grams) switch
    {
        (not null, not null) => EstimateStatus.Available,
        (null, null) => EstimateStatus.Unavailable,
        _ => EstimateStatus.Partial,
    };
}
