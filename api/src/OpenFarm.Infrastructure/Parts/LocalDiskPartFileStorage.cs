using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Namespaced by organizationId first (research.md §8), then content-addressed by the caller's
// precomputed hash (research.md R6, FR-009d): two uploads with byte-identical content collide onto
// the same stored path rather than duplicating. Saving is idempotent; publishing atomically avoids overwriting bytes another worker is reading.
public sealed class LocalDiskPartFileStorage(IOptions<PartFileStorageOptions> options) : IPartFileStorage
{
    public async Task<string> SaveAsync(Guid organizationId, string contentHash, string originalFileName, Stream content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName);
        var relativePath = Path.Combine(organizationId.ToString(), $"{contentHash}{extension}");
        var fullPath = Path.Combine(options.Value.RootDirectory, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (File.Exists(fullPath)) return relativePath;
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var fileStream = File.Create(temporaryPath))
                await content.CopyToAsync(fileStream, cancellationToken);
            try { File.Move(temporaryPath, fullPath, overwrite: false); }
            catch (IOException) when (File.Exists(fullPath)) { /* Another identical upload won. */ }
        }
        finally { File.Delete(temporaryPath); }
        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(options.Value.RootDirectory, storagePath);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task<string> GetRevisionStoragePathAsync(Guid organizationId, string revisionHash, string originalFileName, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(options.Value.RootDirectory, organizationId.ToString());
        var expected = Path.Combine(directory, $"{revisionHash}{Path.GetExtension(originalFileName)}");
        var fullPath = File.Exists(expected) ? expected
            : Directory.EnumerateFiles(directory, $"{revisionHash}.*").FirstOrDefault()
                ?? throw new FileNotFoundException($"Part revision '{revisionHash}' is unavailable.");
        return Task.FromResult(Path.GetRelativePath(options.Value.RootDirectory, fullPath));
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(options.Value.RootDirectory, storagePath);
        File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
