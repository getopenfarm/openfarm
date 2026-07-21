namespace OpenFarm.Application.Parts;

/// <summary>
/// Persists a part's actual uploaded file to disk (research.md §8) — namespaced by
/// <c>organizationId</c> first. Keyed by the owning <c>PartProcessingJob</c>'s own id at submit time
/// (an Add's target <c>Part</c> doesn't exist yet); the resulting opaque <c>storagePath</c> is
/// carried forward unchanged onto the confirmed <c>Part</c> — no copy/move on Confirm.
/// </summary>
public interface IPartFileStorage
{
    Task<string> SaveAsync(Guid organizationId, Guid ownerId, string originalFileName, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken);
}
