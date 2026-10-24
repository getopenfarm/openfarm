using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Production;

public class DispatchProvenanceTests
{
    [Fact]
    public void RoundTripsThePinAndEveryPresetThroughJson()
    {
        var pin = new SlicerRef(SlicerEngine.BambuStudio, "2.7.1");
        ResolvedPreset Preset(ProfileKind kind, string name) =>
            new(kind, Guid.NewGuid(), name, Guid.NewGuid(), name, ProfileSourceFormat.BambuStudio, "2.7.1", "GP004", "Bambulab", ProfileResolutionOutcome.MappedEngine);
        var profiles = new ResolvedSlicingProfiles(pin, "{}", "{}", "{}",
            Preset(ProfileKind.Machine, "Bambu Lab P1S 0.4 nozzle"), Preset(ProfileKind.Process, "0.20mm Standard @BBL X1C"), Preset(ProfileKind.Filament, "Bambu PLA Basic @BBL X1C"));
        var at = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

        var json = DispatchProvenance.From(profiles, "STAMP", at).ToJson();
        var back = DispatchProvenance.FromJson(json)!;

        json.Should().Contain("MappedEngine").And.Contain("BambuStudio").And.Contain("2.7.1");
        back.Pin.Should().Be(pin);
        back.ConfigurationStamp.Should().Be("STAMP");
        back.AtUtc.Should().Be(at);
        back.Presets.Select(p => (p.Kind, p.ResolvedName, p.Outcome)).Should().Equal(
            (ProfileKind.Machine, "Bambu Lab P1S 0.4 nozzle", ProfileResolutionOutcome.MappedEngine),
            (ProfileKind.Process, "0.20mm Standard @BBL X1C", ProfileResolutionOutcome.MappedEngine),
            (ProfileKind.Filament, "Bambu PLA Basic @BBL X1C", ProfileResolutionOutcome.MappedEngine));
    }

    [Fact]
    public void ListsEachDistinctFilamentOfAMultiFilamentSlice()
    {
        var pin = new SlicerRef(SlicerEngine.OrcaSlicer, "2.3.1");
        ResolvedPreset Preset(ProfileKind kind, string name) =>
            new(kind, Guid.Empty, name, Guid.Empty, name, ProfileSourceFormat.OrcaSlicer, "2.3.1", null, "Snapmaker", ProfileResolutionOutcome.Exact);
        var pla = Preset(ProfileKind.Filament, "Snapmaker PLA @U1");
        var petg = Preset(ProfileKind.Filament, "Snapmaker PETG @U1");
        var profiles = new ResolvedSlicingProfiles(pin, "{}", "{}", "{}",
            Preset(ProfileKind.Machine, "Snapmaker U1 (0.4 nozzle)"), Preset(ProfileKind.Process, "0.20 Standard @Snapmaker U1 (0.4 nozzle)"), pla)
        {
            Filaments = [new("{}", pla), new("{}", petg), new("{}", pla)],
        };

        var provenance = DispatchProvenance.From(profiles, "STAMP", DateTimeOffset.UnixEpoch);

        provenance.Presets.Select(p => p.ResolvedName).Should().Equal(
            "Snapmaker U1 (0.4 nozzle)", "0.20 Standard @Snapmaker U1 (0.4 nozzle)", "Snapmaker PLA @U1", "Snapmaker PETG @U1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoJson_IsNoProvenance(string? json) => DispatchProvenance.FromJson(json).Should().BeNull();
}
