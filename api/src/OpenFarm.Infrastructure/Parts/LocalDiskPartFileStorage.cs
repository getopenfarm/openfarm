using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Namespaced by organizationId first (research.md §8). Keyed by an owning id (a PartProcessingJob's
// own id at submit time, per IPartFileStorage's remarks) rather than a Part id, since an Add's
// target Part doesn't exist yet when the file must first be persisted.
public sealed class LocalDiskPartFileStorage(IOptions<PartFileStorageOptions> options) : IPartFileStorage
{
    public async Task<string> SaveAsync(Guid organizationId, Guid ownerId, string originalFileName, Stream content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName);
        var relativePath = Path.Combine(organizationId.ToString(), $"{ownerId}{extension}");
        var fullPath = Path.Combine(options.Value.RootDirectory, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(options.Value.RootDirectory, storagePath);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(options.Value.RootDirectory, storagePath);
        File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
