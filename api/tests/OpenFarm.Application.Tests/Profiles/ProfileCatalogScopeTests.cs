using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

public class ProfileCatalogScopeTests
{
    private static readonly ProfileCatalogScope Scope = new(new Dictionary<ProfileSourceFormat, string>
    {
        [ProfileSourceFormat.OrcaSlicer] = "2.3.1",
        [ProfileSourceFormat.BambuStudio] = "2.7.1",
    });

    [Theory]
    [InlineData(ProfileSourceFormat.OrcaSlicer, "2.3.1", true)]
    [InlineData(ProfileSourceFormat.OrcaSlicer, "2.4.2", false)]
    // Version numbers are per-engine namespaces: Bambu's "2.7.1" must not satisfy Orca's catalog.
    [InlineData(ProfileSourceFormat.OrcaSlicer, "2.7.1", false)]
    [InlineData(ProfileSourceFormat.OrcaSlicer, null, false)]
    [InlineData(ProfileSourceFormat.BambuStudio, "2.7.1", true)]
    [InlineData(ProfileSourceFormat.BambuStudio, "2.3.1", false)]
    public void Includes_SystemRow_OnlyWhenItsFormatsCatalogVersionMatches(ProfileSourceFormat format, string? version, bool expected) =>
        Scope.Includes(ProfileOrigin.System, format, version).Should().Be(expected);

    [Theory]
    [InlineData(ProfileSourceFormat.OrcaSlicer, null)]
    [InlineData(ProfileSourceFormat.OrcaSlicer, "2.4.2")]
    [InlineData(ProfileSourceFormat.BambuStudio, "9.9.9")]
    public void Includes_UserRow_IsVisibleWhateverTheCatalogVersion(ProfileSourceFormat format, string? version) =>
        Scope.Includes(ProfileOrigin.User, format, version).Should().BeTrue();

    [Theory]
    [InlineData(ProfileSourceFormat.OrcaSlicer, null)]
    [InlineData(ProfileSourceFormat.OrcaSlicer, "2.3.1")]
    [InlineData(ProfileSourceFormat.BambuStudio, "2.7.1")]
    public void NoCatalogLoaded_SeesNoSystemRowButKeepsUserRows(ProfileSourceFormat format, string? version)
    {
        var empty = new ProfileCatalogScope(new Dictionary<ProfileSourceFormat, string>());

        empty.Includes(ProfileOrigin.System, format, version).Should().BeFalse();
        empty.Includes(ProfileOrigin.User, format, version).Should().BeTrue();
    }

    [Theory]
    [InlineData(ProfileOrigin.System, ProfileSourceFormat.OrcaSlicer, "2.3.1", true)]
    [InlineData(ProfileOrigin.System, ProfileSourceFormat.BambuStudio, "2.7.1", false)]
    [InlineData(ProfileOrigin.User, ProfileSourceFormat.OrcaSlicer, null, true)]
    [InlineData(ProfileOrigin.User, ProfileSourceFormat.BambuStudio, null, false)]
    public void NativeFormat_ListsOnlyThatFormatsPresets_SystemAndUser(ProfileOrigin origin, ProfileSourceFormat format, string? version, bool expected) =>
        (Scope with { NativeFormat = ProfileSourceFormat.OrcaSlicer }).Includes(origin, format, version).Should().Be(expected);

    [Fact]
    public void Predicate_AgreesWithIncludes_ForEveryOriginFormatVersionCombination()
    {
        var rows = new List<MachineProfile>();
        foreach (var format in Enum.GetValues<ProfileSourceFormat>())
        {
            rows.Add(MachineProfile.Create($"User {format}", format, null, "{}"));
            foreach (var version in new[] { "2.3.1", "2.4.2", "2.7.1" })
            {
                rows.Add(MachineProfile.CreateSystemProfile(
                    $"System {format} {version}", format, null, "{}", "Bambulab", $"{format}-{version}",
                    isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: version));
            }
        }

        var scopes = new[]
        {
            new ProfileCatalogScope(new Dictionary<ProfileSourceFormat, string>()),
            Scope,
            new ProfileCatalogScope(new Dictionary<ProfileSourceFormat, string> { [ProfileSourceFormat.OrcaSlicer] = "2.4.2" }),
            Scope with { NativeFormat = ProfileSourceFormat.OrcaSlicer },
            Scope with { NativeFormat = ProfileSourceFormat.BambuStudio },
            new ProfileCatalogScope(new Dictionary<ProfileSourceFormat, string>()) { NativeFormat = ProfileSourceFormat.BambuStudio },
        };

        foreach (var scope in scopes)
        {
            var viaPredicate = rows.Where(scope.Predicate<MachineProfile>().Compile()).Select(r => r.Name);
            var viaIncludes = rows.Where(r => scope.Includes(r.Origin, r.SourceFormat, r.SourceVersion)).Select(r => r.Name);
            viaPredicate.Should().BeEquivalentTo(viaIncludes);
        }
    }
}
