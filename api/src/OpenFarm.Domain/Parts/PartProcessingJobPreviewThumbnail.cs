namespace OpenFarm.Domain.Parts;

/// <summary>Mirrors <see cref="PartThumbnail"/> for a job's preview (data-model.md, research.md §8). Copied into <see cref="PartThumbnail"/> on Confirm.</summary>
public sealed class PartProcessingJobPreviewThumbnail
{
    private PartProcessingJobPreviewThumbnail(Guid organizationId, Guid jobId, int plateIndex, byte[] bytes)
    {
        OrganizationId = organizationId;
        JobId = jobId;
        PlateIndex = plateIndex;
        Bytes = bytes;
    }

    public Guid OrganizationId { get; private set; }

    public Guid JobId { get; private set; }

    public int PlateIndex { get; private set; }

    public byte[] Bytes { get; private set; }

    public static PartProcessingJobPreviewThumbnail Create(Guid organizationId, Guid jobId, int plateIndex, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Thumbnail bytes must not be empty.", nameof(bytes));
        }

        if (plateIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plateIndex), "Plate index cannot be negative.");
        }

        return new PartProcessingJobPreviewThumbnail(organizationId, jobId, plateIndex, bytes);
    }
}
