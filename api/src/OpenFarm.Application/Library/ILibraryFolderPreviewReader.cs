namespace OpenFarm.Application.Library;

/// <summary>Up to four base64 thumbnails per folder, taken from items anywhere in its subtree.</summary>
public interface ILibraryFolderPreviewReader
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ReadAsync(string library, IReadOnlyList<Guid> folderIds, CancellationToken ct);
}
