namespace OpenFarm.Application.Parts;

/// <summary>
/// Persists a part's actual uploaded file to disk (research.md §8) — namespaced by
/// <c>organizationId</c> first, then content-addressed by the caller-precomputed SHA-256 hex hash
/// of the file's bytes (research.md R6, FR-009d): two uploads that happen to produce
/// byte-identical content naturally collide onto the same stored path rather than duplicating. The
/// resulting opaque <c>storagePath</c> is carried forward unchanged onto the confirmed
/// <c>Part</c>/<c>PrintJob</c> — no copy/move on Confirm.
/// </summary>
public interface IPartFileStorage
{
    Task<string> SaveAsync(Guid organizationId, string contentHash, string originalFileName, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken);

    Task<string> GetRevisionStoragePathAsync(Guid organizationId, string revisionHash, string originalFileName, CancellationToken cancellationToken) =>
        Task.FromResult(Path.Combine(organizationId.ToString(), $"{revisionHash}{Path.GetExtension(originalFileName)}"));

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken);
}
