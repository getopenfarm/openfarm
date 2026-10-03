namespace OpenFarm.Domain.Production;

/// <summary>
/// One material/color breakdown line on a <see cref="PrintJob"/> (spec Key Entities — "a
/// collection, not a single value," FR-013). Created at <see cref="PrintJob.Create"/> time with
/// <see cref="SpoolId"/>/<see cref="GramsConsumed"/> null; filled in via
/// <see cref="Record"/> as the job closes (FR-013a fills it even on failure).
/// </summary>
public sealed class JobFilamentConsumption
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

    public Guid? SpoolId { get; private set; }

    public decimal? GramsConsumed { get; private set; }

    internal static JobFilamentConsumption Create(Guid organizationId, Guid printJobId, string? materialType, string? detectedColorHex) =>
        new(Guid.NewGuid(), organizationId, printJobId, materialType, detectedColorHex);

    internal void Record(Guid? spoolId, decimal gramsConsumed)
    {
        SpoolId = spoolId;
        GramsConsumed = gramsConsumed;
    }
}
