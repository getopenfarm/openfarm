using FluentAssertions;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Domain.Tests.Profiles;

// A System row is always one slicer version's preset: a row with no version could match any catalog.
public class SystemProfileVersionTests
{
    public static TheoryData<string?> BlankVersions => new() { null, "", "  " };

    [Theory]
    [MemberData(nameof(BlankVersions))]
    public void CreateSystemProfile_WithoutASourceVersion_IsRejectedForEveryKind(string? version)
    {
        var machine = () => MachineProfile.CreateSystemProfile(
            "M", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GM001", true, null, null, version!);
        var process = () => ProcessProfile.CreateSystemProfile(
            "P", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP001", true, null, version!);
        var filament = () => FilamentProfile.CreateSystemProfile(
            "F", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GF001", true, null, "PLA", null, version!);

        machine.Should().Throw<ArgumentException>().WithParameterName("sourceVersion");
        process.Should().Throw<ArgumentException>().WithParameterName("sourceVersion");
        filament.Should().Throw<ArgumentException>().WithParameterName("sourceVersion");
    }

    [Fact]
    public void CreateSystemProfile_KeepsTheVersion_AndUserRowsHaveNone()
    {
        var system = MachineProfile.CreateSystemProfile(
            "M", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GM001", true, null, null, "2.4.2");
        var user = MachineProfile.Create("Mine", ProfileSourceFormat.OrcaSlicer, null, "{}");

        system.SourceVersion.Should().Be("2.4.2");
        user.SourceVersion.Should().BeNull();
    }
}
