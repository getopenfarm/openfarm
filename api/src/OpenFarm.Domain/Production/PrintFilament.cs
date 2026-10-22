using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Production;

/// <summary>
/// One material/color line of a <see cref="Print"/> (spec Key Entities — "a collection, not a single value," FR-013). On a
/// dispatched job it is created from the job's filament requirements; on an external print, from the file the printer printed.
/// Created with <see cref="SpoolId"/>/<see cref="GramsConsumed"/> null; filled in via <see cref="Record"/> as the print closes
/// (FR-013a fills it even on failure).
/// </summary>
public sealed class PrintFilament : IFilamentUsageLine
{
    private PrintFilament(Guid id, Guid printId, int toolIndex, string? materialType, string? detectedColorHex)
    {
        Id = id;
        PrintId = printId;
        ToolIndex = toolIndex;
        MaterialType = materialType;
        DetectedColorHex = detectedColorHex;
    }

    public Guid Id { get; }

    public Guid PrintId { get; }

    /// <summary>Position within the print: the slicer's tool for an external print, requirement order for a job.</summary>
    public int ToolIndex { get; }

    public string? MaterialType { get; }

    public string? DetectedColorHex { get; }

    public Guid? NarrowFilamentVariantId { get; private set; }
    public Guid? NarrowFilamentProfileId { get; private set; }
    public JobFilamentRequirementSpec Requirement => new(MaterialType, DetectedColorHex, NarrowFilamentVariantId, NarrowFilamentProfileId);

    public Guid? SpoolId { get; private set; }

    public decimal? GramsConsumed { get; private set; }

    /// <summary>The slicer's own full-print estimate for this line, read from the print's file; null when it couldn't be read.</summary>
    public decimal? EstimatedGrams { get; private set; }

    /// <summary>Null when the file's G-code couldn't be read; a partial print then deducts by an even spread.</summary>
    public FilamentUsageCurve? UsageCurve { get; private set; }

    /// <summary>Why automatic deduction left this line unrecorded; null once it is recorded or if nothing went wrong.</summary>
    public FilamentSkipReason? SkippedReason { get; private set; }

    /// <summary>The price per kg this line is costed at, written once when it is first deducted; null while no price was known.</summary>
    public decimal? PricePerKg { get; private set; }

    /// <summary>Whether <see cref="PricePerKg"/> came from the spool, its colour or its filament; null with the price.</summary>
    public FilamentPriceSource? PriceSource { get; private set; }

    /// <summary>What the grams deducted so far cost at <see cref="PricePerKg"/>, unrounded; null when either is unknown. Round only for display.</summary>
    public decimal? Cost => GramsConsumed is { } grams && PricePerKg is { } price ? grams * price / 1000m : null;

    internal static PrintFilament Create(Guid printId, int toolIndex, string? materialType, string? detectedColorHex) =>
        new(Guid.NewGuid(), printId, toolIndex, materialType, detectedColorHex);

    internal static PrintFilament Create(Guid printId, int toolIndex, JobFilamentRequirementSpec requirement) =>
        new(Guid.NewGuid(), printId, toolIndex, requirement.MaterialType, requirement.DetectedColorHex)
        {
            NarrowFilamentVariantId = requirement.NarrowFilamentVariantId,
            NarrowFilamentProfileId = requirement.NarrowFilamentProfileId
        };

    internal static PrintFilament Create(Guid printId, ExternalFilamentSpec spec) =>
        new(Guid.NewGuid(), printId, spec.ToolIndex, spec.MaterialType, spec.ColorHex)
        {
            EstimatedGrams = spec.Grams,
            UsageCurve = spec.Curve
        };

    internal void Bind(Guid? spoolId) => SpoolId = spoolId;

    internal void BindUsage(Guid? spoolId, decimal? estimatedGrams, FilamentUsageCurve? usageCurve)
    {
        SpoolId = spoolId;
        EstimatedGrams = estimatedGrams;
        UsageCurve = usageCurve;
    }

    internal void Skip(FilamentSkipReason reason) => SkippedReason = reason;

    /// <summary>Keeps the first price a line is costed at: a later one, from a price change or a re-run, never replaces it.</summary>
    internal void SnapshotPrice(ResolvedFilamentPrice price)
    {
        if (PricePerKg is not null)
        {
            return;
        }

        PricePerKg = price.PricePerKg;
        PriceSource = price.Source;
    }

    internal void Record(Guid? spoolId, decimal gramsConsumed)
    {
        SpoolId = spoolId;
        GramsConsumed = gramsConsumed;
        SkippedReason = null;
    }
}

public sealed record ExternalFilamentSpec(int ToolIndex, string? MaterialType, string? ColorHex, decimal? Grams, FilamentUsageCurve? Curve);
