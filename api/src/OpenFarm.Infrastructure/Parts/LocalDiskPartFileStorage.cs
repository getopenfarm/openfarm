using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Namespaced by organizationId first (research.md §8), then content-addressed by the caller's
// precomputed hash (research.md R6, FR-009d): two uploads with byte-identical content collide onto
// the same stored path rather than duplicating. Saving is idempotent by construction — a path that
// already exists is simply overwritten with identical bytes, never duplicated.
public sealed class LocalDiskPartFileStorage(IOptions<PartFileStorageOptions> options) : IPartFileStorage
{
    public async Task<string> SaveAsync(Guid organizationId, string contentHash, string originalFileName, Stream content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName);
        var relativePath = Path.Combine(organizationId.ToString(), $"{contentHash}{extension}");
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
