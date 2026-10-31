using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>Preview evidence uses the same per-plate detector as confirmation, including before estimates finish.</summary>
public sealed record PartProcessingQuantity(int? UnitsPerPrint, IReadOnlyDictionary<int, int?> Plates)
{
    public static async Task<PartProcessingQuantity> ReadAsync(PartProcessingJob job, IPartFileStorage? storage, CancellationToken ct)
    {
        var counts = new Dictionary<int, int?>();
        if (storage is null) return new(null, counts);
        try
        {
            await using var source = await storage.OpenReadAsync(job.StoragePath, ct);
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            foreach (var plate in job.PreviewPlates)
            {
                buffer.Position = 0;
                counts[plate.Number] = PartObjectInventory.Read(buffer, job.OriginalFileName, plate.Number).Count;
            }
            if (counts.Count == 1) return new(counts.Values.Single(), counts);
            if (counts.Count > 1) return new(null, counts);
            buffer.Position = 0;
            return new(PartObjectInventory.Read(buffer, job.OriginalFileName).Count, counts);
        }
        catch (IOException) { return new(null, counts); }
    }
}
