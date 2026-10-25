using FluentAssertions;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Infrastructure.Tests.Auth;

public class SigningKeyFileTests
{
    [Fact]
    public void LoadOrCreate_GeneratesOnceAndReturnsTheSameKeyEverywhere()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var keys = Enumerable.Range(0, 10).AsParallel().Select(_ => SigningKeyFile.LoadOrCreate(directory)).ToList();

            keys.Distinct().Should().ContainSingle();
            keys[0].Length.Should().BeGreaterThanOrEqualTo(64);
            SigningKeyFile.LoadOrCreate(directory).Should().Be(keys[0]);
            Directory.GetFiles(directory).Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
