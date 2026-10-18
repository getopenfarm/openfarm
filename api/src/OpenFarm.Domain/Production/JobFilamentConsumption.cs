namespace OpenFarm.Domain.Production;

/// <summary>
/// One material/color breakdown line on a <see cref="PrintJob"/> (spec Key Entities — "a
/// collection, not a single value," FR-013). Created at <see cref="PrintJob.Create"/> time with
/// <see cref="SpoolId"/>/<see cref="GramsConsumed"/> null; filled in via
/// <see cref="Record"/> as the job closes (FR-013a fills it even on failure).
/// </summary>
public sealed class JobFilamentConsumption : IFilamentUsageLine
{
    private JobFilamentConsumption(Guid id, Guid organizationId, Guid printJobId, string? materialType, string? detectedColorHex)
    {
        Id = id;
        OrganizationId = organizationId;
        PrintJobId = printJobId;
        MaterialType = materialType;
        DetectedColorHex = detectedColorHex;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid PrintJobId { get; }

    public string? MaterialType { get; }

    public string? DetectedColorHex { get; }

    public Guid? NarrowFilamentVariantId { get; private set; }
    public Guid? NarrowFilamentProfileId { get; private set; }
    public JobFilamentRequirementSpec Requirement => new(MaterialType, DetectedColorHex, NarrowFilamentVariantId, NarrowFilamentProfileId);

    public Guid? SpoolId { get; private set; }

    public decimal? GramsConsumed { get; private set; }

    /// <summary>The slicer's own full-job estimate for this line, read from the dispatched file; null when it couldn't be read.</summary>
    public decimal? EstimatedGrams { get; private set; }

    /// <summary>Null when the file's G-code couldn't be read; a partial print then deducts by an even spread.</summary>
    public FilamentUsageCurve? UsageCurve { get; private set; }

    internal static JobFilamentConsumption Create(Guid organizationId, Guid printJobId, string? materialType, string? detectedColorHex) =>
        new(Guid.NewGuid(), organizationId, printJobId, materialType, detectedColorHex);

    internal static JobFilamentConsumption Create(Guid organizationId, Guid printJobId, JobFilamentRequirementSpec requirement) =>
        new(Guid.NewGuid(), organizationId, printJobId, requirement.MaterialType, requirement.DetectedColorHex)
        {
            NarrowFilamentVariantId = requirement.NarrowFilamentVariantId,
            NarrowFilamentProfileId = requirement.NarrowFilamentProfileId
        };

    internal void BindUsage(Guid? spoolId, decimal? estimatedGrams, FilamentUsageCurve? usageCurve)
    {
        SpoolId = spoolId;
        EstimatedGrams = estimatedGrams;
        UsageCurve = usageCurve;
    }

    internal void Record(Guid? spoolId, decimal gramsConsumed)
    {
        SpoolId = spoolId;
        GramsConsumed = gramsConsumed;
    }
}
