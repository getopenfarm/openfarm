using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

public class LocalDiskPartFileStorageTests
{
    [Fact]
    public async Task RepeatedAndConcurrentUploads_DoNotOverwriteAnOpenRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var storage = new LocalDiskPartFileStorage(Options.Create(new PartFileStorageOptions { RootDirectory = root }));
            var bytes = Encoding.UTF8.GetBytes("immutable revision");
            var paths = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                using var input = new MemoryStream(bytes);
                return await storage.SaveAsync("hash", "part.gcode", input, default);
            }));
            paths.Distinct().Should().ContainSingle();
            await using var reader = await storage.OpenReadAsync(paths[0], default);
            using var repeated = new MemoryStream(bytes);
            (await storage.SaveAsync("hash", "part.gcode", repeated, default)).Should().Be(paths[0]);
            using var text = new StreamReader(reader);
            (await text.ReadToEndAsync()).Should().Be("immutable revision");
            Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }
}
