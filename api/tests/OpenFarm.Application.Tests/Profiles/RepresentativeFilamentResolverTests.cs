using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

// TDD (write-first) — research.md §2/FR-018/FR-019: the deterministic tie-break among System,
// selectable, currently-offered filament rows matching a material type must be stable across
// repeated calls and across ingestion re-runs (keyed on VendorName+ExternalId, never Name).
public class RepresentativeFilamentResolverTests
{
    private static FilamentProfile CreateSystemFilament(
        string vendorName, string externalId, string materialType, bool isSelectable = true, bool isCurrentlyOffered = true)
    {
        var profile = FilamentProfile.CreateSystemProfile(
            Guid.NewGuid(), $"{vendorName} {materialType} {externalId}", ProfileSourceFormat.OrcaSlicer, null,
            $$"""{"type":"filament","filament_type":["{{materialType}}"]}""",
            vendorName, externalId, isSelectable, compatibleMachineNames: null, materialType: materialType, includedTemplateNames: null);

        if (!isCurrentlyOffered)
        {
            profile.MarkStale();
        }

        return profile;
    }

    [Fact]
    public async Task ResolveAsync_MultipleCandidatesForSameMaterial_PicksSmallestVendorNameThenExternalIdOrdinal()
    {
        var repo = new FakeFilamentProfileRepository();
        await repo.AddAsync(CreateSystemFilament("Zzz Vendor", "Z001", "PLA"), CancellationToken.None);
        var expected = CreateSystemFilament("Bambulab", "A001", "PLA");
        await repo.AddAsync(expected, CancellationToken.None);
        await repo.AddAsync(CreateSystemFilament("Bambulab", "B001", "PLA"), CancellationToken.None);

        var resolver = new RepresentativeFilamentResolver(repo);

        var result = await resolver.ResolveAsync("PLA", CancellationToken.None);

        result.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task ResolveAsync_RepeatedCalls_ReturnIdenticalResult()
    {
        var repo = new FakeFilamentProfileRepository();
        await repo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PLA"), CancellationToken.None);
        await repo.AddAsync(CreateSystemFilament("Bambulab", "B001", "PLA"), CancellationToken.None);

        var resolver = new RepresentativeFilamentResolver(repo);

        var first = await resolver.ResolveAsync("PLA", CancellationToken.None);
        var second = await resolver.ResolveAsync("PLA", CancellationToken.None);

        first.Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task ResolveAsync_NoCandidateForMaterial_ThrowsSlicingProfileUnconfigured()
    {
        var repo = new FakeFilamentProfileRepository();
        await repo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PETG"), CancellationToken.None);

        var resolver = new RepresentativeFilamentResolver(repo);

        var act = async () => await resolver.ResolveAsync("PLA", CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_MatchingCandidateNotCurrentlyOffered_IsExcluded()
    {
        var repo = new FakeFilamentProfileRepository();
        await repo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PLA", isCurrentlyOffered: false), CancellationToken.None);
        var expected = CreateSystemFilament("Bambulab", "B001", "PLA");
        await repo.AddAsync(expected, CancellationToken.None);

        var resolver = new RepresentativeFilamentResolver(repo);

        var result = await resolver.ResolveAsync("PLA", CancellationToken.None);

        result.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task ResolveAsync_UserOriginCandidateForSameMaterial_IsExcluded()
    {
        var repo = new FakeFilamentProfileRepository();
        var userProfile = FilamentProfile.Create(Guid.NewGuid(), "My Tuned PLA", ProfileSourceFormat.OrcaSlicer, null, """{"filament_type":["PLA"]}""");
        await repo.AddAsync(userProfile, CancellationToken.None);
        var expected = CreateSystemFilament("Bambulab", "A001", "PLA");
        await repo.AddAsync(expected, CancellationToken.None);

        var resolver = new RepresentativeFilamentResolver(repo);

        var result = await resolver.ResolveAsync("PLA", CancellationToken.None);

        result.Id.Should().Be(expected.Id);
    }
}
