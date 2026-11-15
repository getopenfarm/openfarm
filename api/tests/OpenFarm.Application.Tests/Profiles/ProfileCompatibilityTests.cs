using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

public sealed class ProfileCompatibilityTests
{
    [Fact]
    public async Task FilamentPickerUsesEffectiveInheritedMaterialForUserProfiles()
    {
        var profiles = new FakeFilamentProfileRepository();
        var parent = FilamentProfile.Create("PLA base", ProfileSourceFormat.OrcaSlicer, null, """{"filament_type":["PLA"]}""");
        var child = FilamentProfile.Create("My PLA", ProfileSourceFormat.OrcaSlicer, "PLA base", "{}");
        child.ResolveBaseProfile(parent.Id);
        await profiles.AddAsync(parent, default); await profiles.AddAsync(child, default);
        var handler = new GetFilamentProfilesQueryHandler(profiles, new StubProfileBundleParser(), new ProfileCompatibility(new FakeMachineProfileRepository(), profiles), new FakeSlicingDefaultsQuery());
        (await handler.HandleAsync(new(Origin: "All", MaterialType: "PLA", Brand: "Bambu", ModelCode: "P1S", NozzleDiameterMm: 0.4m), default))
            .Items.Should().Contain(p => p.Id == child.Id && p.MaterialType == "PLA");
    }

    [Fact]
    public async Task FilamentCompatibilityRejectsMaterialAndNozzleMismatchesButDoesNotConfuseSourceWithHardware()
    {
        var profiles = new FakeFilamentProfileRepository();
        var pla = FilamentProfile.Create("PLA 0.4", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"],"nozzle_diameter":["0.4"]}""");
        await profiles.AddAsync(pla, default);
        var compatibility = new ProfileCompatibility(new FakeMachineProfileRepository(), profiles);
        await ((Func<Task>)(() => compatibility.ValidateFilamentAsync(pla, "PETG", "Bambu", "P1S", 0.4m, default))).Should().ThrowAsync<SlicingProfileUnconfiguredException>();
        await ((Func<Task>)(() => compatibility.ValidateFilamentAsync(pla, "PLA", "Bambu", "P1S", 0.6m, default))).Should().ThrowAsync<SlicingProfileUnconfiguredException>();
        var differentFormat = MachineProfile.Create("P1S 0.4", ProfileSourceFormat.BambuStudio, null, "{}");
        await compatibility.ValidateFilamentAsync(pla, "PLA", "Bambu", "P1S", 0.4m, default, differentFormat);
    }

    [Theory]
    [InlineData(SlicerEngine.OrcaSlicer, ProfileSourceFormat.BambuStudio, false)]
    [InlineData(SlicerEngine.OrcaSlicer, ProfileSourceFormat.OrcaSlicer, true)]
    [InlineData(SlicerEngine.BambuStudio, ProfileSourceFormat.OrcaSlicer, false)]
    [InlineData(SlicerEngine.BambuStudio, ProfileSourceFormat.BambuStudio, true)]
    [InlineData(SlicerEngine.SuperSlicer, ProfileSourceFormat.BambuStudio, false)]
    [InlineData(SlicerEngine.SuperSlicer, ProfileSourceFormat.OrcaSlicer, false)]
    public void EngineSupportsOnlyItsOwnPresetFormat(SlicerEngine engine, ProfileSourceFormat source, bool supported) =>
        ProfileEngineCompatibility.Supports(engine, source).Should().Be(supported);

    [Theory]
    [InlineData(SlicerEngine.OrcaSlicer, 1, "Z Orca")]
    [InlineData(SlicerEngine.BambuStudio, 1, "A Bambu")]
    public async Task FleetPickerListsOnlyTheSelectedEnginesPresetsBeforePagination(SlicerEngine engine, int count, string firstName)
    {
        var profiles = new FakeFilamentProfileRepository();
        await profiles.AddAsync(FilamentProfile.Create("A Bambu", ProfileSourceFormat.BambuStudio, null,
            """{"filament_type":["PLA"]}"""), default);
        await profiles.AddAsync(FilamentProfile.Create("Z Orca", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"]}"""), default);
        var handler = new GetFilamentProfilesQueryHandler(profiles, new StubProfileBundleParser(),
            new ProfileCompatibility(new FakeMachineProfileRepository(), profiles), new FakeSlicingDefaultsQuery(engine));
        var page = await handler.HandleAsync(new(Origin: "All", MaterialType: "PLA", Brand: "Bambu", ModelCode: "P1S",
            NozzleDiameterMm: 0.4m, PageSize: 1), default);
        page.TotalCount.Should().Be(count);
        page.Items.Should().ContainSingle().Which.Name.Should().Be(firstName);
    }

    [Fact]
    public async Task SharedX1CNamedBambuPresetMatchesAnOrcaP1SByDeclaredCompatibility()
    {
        var profiles = new FakeFilamentProfileRepository();
        var machines = new FakeMachineProfileRepository();
        var machine = MachineProfile.Create("Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null,
            """{"printer_model":"Bambu Lab P1S","nozzle_diameter":["0.4"]}""");
        var filament = FilamentProfile.Create("Bambu PLA Basic @BBL X1C", ProfileSourceFormat.BambuStudio, null,
            """{"filament_type":["PLA"],"compatible_printers":["Bambu Lab P1S 0.4 nozzle"]}""");
        await machines.AddAsync(machine, default);
        await profiles.AddAsync(filament, default);
        var compatibility = new ProfileCompatibility(machines, profiles);
        await compatibility.ValidateFilamentAsync(filament, "PLA", "Bambu", "P1S", 0.4m, default, machine);
        await ((Func<Task>)(() => compatibility.ValidateFilamentAsync(filament, "PLA", "Bambu", "P1S", 0.6m, default, machine)))
            .Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }
    [Fact]
    public async Task ExplicitFilamentSortIsAppliedBeforePagination_AndTheOtherEnginesPresetsStayOut()
    {
        var profiles = new FakeFilamentProfileRepository();
        await profiles.AddAsync(FilamentProfile.Create("Zulu Orca", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"]}"""), default);
        await profiles.AddAsync(FilamentProfile.Create("alpha Orca", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"]}"""), default);
        await profiles.AddAsync(FilamentProfile.Create("Bambu only", ProfileSourceFormat.BambuStudio, null,
            """{"filament_type":["PLA"]}"""), default);
        var handler = new GetFilamentProfilesQueryHandler(profiles, new StubProfileBundleParser(),
            new ProfileCompatibility(new FakeMachineProfileRepository(), profiles), new FakeSlicingDefaultsQuery(SlicerEngine.OrcaSlicer));
        var defaults = new GetFilamentProfilesQuery(Origin: "All", MaterialType: "PLA", Brand: "Bambu", ModelCode: "P1S",
            NozzleDiameterMm: 0.4m, PageSize: 1);
        (await handler.HandleAsync(defaults, default)).Items.Should().ContainSingle().Which.Name.Should().Be("alpha Orca");
        var sorted = defaults with { Sorting = new OpenFarm.Application.Common.ListSort("name", true) };
        var first = await handler.HandleAsync(sorted, default);
        first.TotalCount.Should().Be(2);
        first.Items.Should().ContainSingle().Which.Name.Should().Be("Zulu Orca");
        (await handler.HandleAsync(sorted with { Page = 2 }, default)).Items.Should().ContainSingle().Which.Name.Should().Be("alpha Orca");
    }

    private sealed class StubProfileBundleParser : IProfileBundleParser
    {
        public Task<ParsedProfileBundle> ParseAsync(Stream bundleStream, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) => throw new NotSupportedException();
        public MachineProfileDisplayFields ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) => new(null, null, null, null, null, []);
        public FilamentProfileDisplayFields ExtractFilamentDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat, string name, bool hasBaseProfileName) => new(null, null, null, null, null, null, null, null, []);
        public ProcessProfileDisplayFields ExtractProcessDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) => new(null, null, []);
    }
}
