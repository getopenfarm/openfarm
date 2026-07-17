using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

// Returns a pre-built bundle regardless of the stream's actual content — the resolution algorithm
// under test doesn't care about real bundle parsing (that's ProfileBundleParserTests' job).
public sealed class FakeProfileBundleParser : IProfileBundleParser
{
    public ParsedProfileBundle Bundle { get; set; } = new([], []);

    public Task<ParsedProfileBundle> ParseAsync(
        Stream bundleStream, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) =>
        Task.FromResult(Bundle);

    public MachineProfileDisplayFields ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) =>
        throw new NotSupportedException("Not needed by the resolution-focused tests using this fake.");

    public FilamentProfileDisplayFields ExtractFilamentDisplayFields(
        string rawDataJson, ProfileSourceFormat sourceFormat, string name, bool hasBaseProfileName) =>
        throw new NotSupportedException("Not needed by the resolution-focused tests using this fake.");
}
