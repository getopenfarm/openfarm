namespace OpenFarm.Application.Parts;

/// <summary>Compatibility suggestion. Unknown is kept separate from a proven singleton by PartObjectInventory.</summary>
public static class SlicedFileUnitsPerPrint
{
    public static async Task<int> SuggestAsync(IPartFileStorage storage, string path, int? plate, CancellationToken ct)
        => (await PartObjectInventory.ReadAsync(storage, path, plate, ct)).Count ?? 1;

    public static int Suggest(Stream stream, string fileName, int? plate = null)
        => PartObjectInventory.Read(stream, fileName, plate).Count ?? 1;
}
