namespace OpenFarm.Domain.Production;

/// <summary>One filament of a print — on a dispatched job or an external print — as automatic deduction reads it.</summary>
public interface IFilamentUsageLine
{
    Guid Id { get; }

    Guid? SpoolId { get; }

    decimal? EstimatedGrams { get; }

    decimal? GramsConsumed { get; }

    FilamentUsageCurve? UsageCurve { get; }
}
