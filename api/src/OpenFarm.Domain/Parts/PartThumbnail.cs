namespace OpenFarm.Domain.Parts;

/// <summary>Input to <see cref="Part.Create"/>/<see cref="Part.ReplaceFile"/> — a not-yet-persisted per-plate thumbnail.</summary>
public sealed record ThumbnailSpec(int PlateIndex, byte[] Bytes);

/// <summary>
/// One row per plate that successfully produced a thumbnail (data-model.md, research.md §8) —
/// never one row per plate that merely exists. Always PNG, normalized at ingestion.
/// </summary>
public sealed class PartThumbnail
{
    private PartThumbnail(Guid partId, int plateIndex, byte[] bytes)
    {
        PartId = partId;
        PlateIndex = plateIndex;
        Bytes = bytes;
    }

    public Guid PartId { get; private set; }

    public int PlateIndex { get; private set; }

    public byte[] Bytes { get; private set; }

    internal void ReplaceBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Thumbnail bytes must not be empty.", nameof(bytes));
        }

        Bytes = bytes;
    }

    public static PartThumbnail Create(Guid partId, int plateIndex, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Thumbnail bytes must not be empty.", nameof(bytes));
        }

        if (plateIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plateIndex), "Plate index cannot be negative.");
        }

        return new PartThumbnail(partId, plateIndex, bytes);
    }
}
