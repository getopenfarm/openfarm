using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Profiles;

// The decision table of ProfileResolver: a selected preset resolves to the pinned slicer's own catalog or is blocked —
// never to another engine's, another version's or a similarly named preset.
public class ProfileResolverTests
{
    private static readonly SlicerRef SuperSlicer = new(SlicerEngine.SuperSlicer, "2.5.59.13");

    private static async Task<ProfileResolution<FilamentProfile>> ResolveAsync(TwoEngineCatalog catalog, FilamentProfile selected, SlicerRef pin) =>
        await catalog.Resolver.ResolveAsync(selected, pin, default);

    // ---------------------------------------------------------------- same catalog

    [Theory]
    [InlineData("OrcaSlicer", "2.3.1")]
    [InlineData("OrcaSlicer", "2.4.2")]
    [InlineData("BambuStudio", "2.7.1")]
    public async Task SystemRowAlreadyInThePinnedCatalog_IsItself(string engine, string version)
    {
        var pin = new SlicerRef(Enum.Parse<SlicerEngine>(engine), version);
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(pin);

        var machineResult = await catalog.Resolver.ResolveAsync(machine[pin], pin, default);
        var processResult = await catalog.Resolver.ResolveAsync(process[pin], pin, default);
        var filamentResult = await ResolveAsync(catalog, filament[pin], pin);

        machineResult.Outcome.Should().Be(ProfileResolutionOutcome.Exact);
        processResult.Outcome.Should().Be(ProfileResolutionOutcome.Exact);
        filamentResult.Outcome.Should().Be(ProfileResolutionOutcome.Exact);
        machineResult.Resolved.Should().BeSameAs(machine[pin]);
        processResult.Resolved.Should().BeSameAs(process[pin]);
        filamentResult.Resolved.Should().BeSameAs(filament[pin]);
    }

    [Fact]
    public async Task SystemRowNoLongerOfferedInThePinnedCatalog_IsBlocked()
    {
        var catalog = new TwoEngineCatalog();
        var filament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Bambu271)[Bambu271];
        filament.MarkStale();

        var result = await ResolveAsync(catalog, filament, Bambu271);

        result.Outcome.Should().Be(ProfileResolutionOutcome.NotOffered);
        result.Resolved.Should().BeNull();
        result.Message.Should().Contain("Filament profile 'Bambu PLA Basic @BBL X1C'").And.Contain("Bambu Studio 2.7.1");
    }

    // ---------------------------------------------------------------- same engine, another version

    [Fact]
    public async Task SameEngineOtherVersion_FollowsTheSettingId()
    {
        var catalog = new TwoEngineCatalog();
        var filament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231, Orca242);

        var result = await ResolveAsync(catalog, filament[Orca231], Orca242);

        result.Outcome.Should().Be(ProfileResolutionOutcome.MappedVersion);
        result.Resolved.Should().BeSameAs(filament[Orca242]);
    }

    [Fact]
    public async Task SameEngineOtherVersion_ToleratesARename()
    {
        var catalog = new TwoEngineCatalog();
        var old = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231)[Orca231];
        var renamed = catalog.Filament(BambuVendor, "Bambu PLA Basic (renamed) @BBL X1C", "GFSA00", null, Orca242)[Orca242];

        var result = await ResolveAsync(catalog, old, Orca242);

        result.Outcome.Should().Be(ProfileResolutionOutcome.MappedVersion);
        result.Resolved.Should().BeSameAs(renamed);
    }

    [Fact]
    public async Task SameSettingIdUnderAnotherVendor_IsNotTheSamePreset()
    {
        var catalog = new TwoEngineCatalog();
        var old = catalog.Filament(BambuVendor, "Shared PLA", "GFX01", null, Orca231)[Orca231];
        catalog.Filament("Generic", "Shared PLA", "GFX01", null, Orca242);
        catalog.Loaded(Orca242);

        var result = await ResolveAsync(catalog, old, Orca242);

        result.Outcome.Should().Be(ProfileResolutionOutcome.Missing);
        result.Resolved.Should().BeNull();
    }

    [Fact]
    public async Task PresetWithoutSettingId_FollowsVendorAndNameWithinTheEngine()
    {
        var catalog = new TwoEngineCatalog();
        var old = catalog.Filament(BambuVendor, "Bambu PLA Basic @base", null, null, Orca231)[Orca231];
        var current = catalog.Filament(BambuVendor, "Bambu PLA Basic @base", null, null, Orca242)[Orca242];
        catalog.Filament("Generic", "Bambu PLA Basic @base", null, null, Orca242);

        var result = await ResolveAsync(catalog, old, Orca242);

        result.Outcome.Should().Be(ProfileResolutionOutcome.MappedVersion);
        result.Resolved.Should().BeSameAs(current);
    }

    [Fact]
    public async Task TwoPresetsMatchingEquallyWell_AreAmbiguousNotFirstWins()
    {
        var catalog = new TwoEngineCatalog();
        var old = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231)[Orca231];
        catalog.Filament(BambuVendor, "Bambu PLA Basic (a) @BBL X1C", "GFSA00", null, Orca242);
        catalog.Filament(BambuVendor, "Bambu PLA Basic (b) @BBL X1C", "GFSA00", null, Orca242);

        var result = await ResolveAsync(catalog, old, Orca242);

        result.Outcome.Should().Be(ProfileResolutionOutcome.Ambiguous);
        result.Resolved.Should().BeNull();
    }

    [Fact]
    public async Task ExactNameBeatsARename_WhenBothShareTheSettingId()
    {
        var catalog = new TwoEngineCatalog();
        var old = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231)[Orca231];
        catalog.Filament(BambuVendor, "Bambu PLA Basic (renamed) @BBL X1C", "GFSA00", null, Orca242);
        var sameName = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca242)[Orca242];

        var result = await ResolveAsync(catalog, old, Orca242);

        result.Resolved.Should().BeSameAs(sameName);
    }

    [Fact]
    public async Task TwinThatIsStaleOrNotSelectableInThePinnedCatalog_IsBlocked()
    {
        var catalog = new TwoEngineCatalog();
        var old = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231)[Orca231];
        var twin = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca242)[Orca242];
        twin.MarkStale();

        var result = await ResolveAsync(catalog, old, Orca242);

        result.Outcome.Should().Be(ProfileResolutionOutcome.NotOffered);
        result.Resolved.Should().BeNull();
    }

    // ---------------------------------------------------------------- other engine: the exact twin only

    [Fact]
    public async Task OrcaSelection_PinnedToBambuStudio_ResolvesToBambuStudiosOwnTwin()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231, Bambu271);

        (await catalog.Resolver.ResolveAsync(machine[Orca231], Bambu271, default)).Resolved.Should().BeSameAs(machine[Bambu271]);
        (await catalog.Resolver.ResolveAsync(process[Orca231], Bambu271, default)).Resolved.Should().BeSameAs(process[Bambu271]);
        var result = await ResolveAsync(catalog, filament[Orca231], Bambu271);

        result.Outcome.Should().Be(ProfileResolutionOutcome.MappedEngine);
        result.Resolved.Should().BeSameAs(filament[Bambu271]);
        result.Resolved!.RawDataJson.Should().Contain(Marker("FILAMENT", "Bambu PLA Basic @BBL X1C", Bambu271));
    }

    [Fact]
    public async Task BambuSelection_PinnedToOrca_ResolvesToOrcasOwnTwin()
    {
        var catalog = new TwoEngineCatalog();
        var (_, process, _) = catalog.StandardSet(Orca231, Bambu271);

        var result = await catalog.Resolver.ResolveAsync(process[Bambu271], Orca231, default);

        result.Outcome.Should().Be(ProfileResolutionOutcome.MappedEngine);
        result.Resolved.Should().BeSameAs(process[Orca231]);
    }

    [Fact]
    public async Task PresetOnlyTheSelectedEngineShips_IsMissingUnderTheOtherEngine_NeverSubstituted()
    {
        var catalog = new TwoEngineCatalog();
        var orcaOnly = catalog.Process(BambuVendor, "0.16mm Orca Only @BBL X1C", "GPOO1", null, Orca231)[Orca231];
        // The other engine has a similarly named and a same-id preset — neither is the same preset.
        catalog.Process(BambuVendor, "0.16mm Orca Only @BBL X1C (Bambu)", "GPOO1", null, Bambu271);
        catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", null, Bambu271);

        var result = await catalog.Resolver.ResolveAsync(orcaOnly, Bambu271, default);

        result.Outcome.Should().Be(ProfileResolutionOutcome.Missing);
        result.Resolved.Should().BeNull();
        result.Message.Should().Contain("Process profile '0.16mm Orca Only @BBL X1C' is not in Bambu Studio 2.7.1");
    }

    [Fact]
    public async Task RenamedAcrossEngines_HasNoTrailAndIsMissing()
    {
        var catalog = new TwoEngineCatalog();
        var orca = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231)[Orca231];
        catalog.Filament(BambuVendor, "Bambu PLA Basic (Bambu name) @BBL X1C", "GFSA00", null, Bambu271);

        var result = await ResolveAsync(catalog, orca, Bambu271);

        result.Outcome.Should().Be(ProfileResolutionOutcome.Missing);
    }

    [Fact]
    public async Task SameIdUnderAnotherVendor_IsNeverATwin()
    {
        var catalog = new TwoEngineCatalog();
        var orca = catalog.Filament("Generic", "Generic PLA", "GFL99", null, Orca231)[Orca231];
        catalog.Filament(BambuVendor, "Generic PLA", "GFL99", null, Bambu271);

        (await ResolveAsync(catalog, orca, Bambu271)).Outcome.Should().Be(ProfileResolutionOutcome.Missing);
    }

    [Fact]
    public async Task PresetWithoutSettingId_NeverCrossesEngines()
    {
        var catalog = new TwoEngineCatalog();
        var orca = catalog.Filament(BambuVendor, "Bambu PLA Basic @base", null, null, Orca231)[Orca231];
        catalog.Filament(BambuVendor, "Bambu PLA Basic @base", null, null, Bambu271);

        (await ResolveAsync(catalog, orca, Bambu271)).Outcome.Should().Be(ProfileResolutionOutcome.Missing);
    }

    // ---------------------------------------------------------------- no data, no guess

    [Theory]
    [InlineData("OrcaSlicer", "9.9.9", "Orca231")]
    [InlineData("BambuStudio", "9.9.9", "Orca231")]
    [InlineData("OrcaSlicer", "9.9.9", "Bambu271")]
    public async Task PinnedCatalogNeverIngested_IsBlockedWithoutFallingBackToTheStoredRow(string engine, string version, string selectedIn)
    {
        var pin = new SlicerRef(Enum.Parse<SlicerEngine>(engine), version);
        var catalog = new TwoEngineCatalog();
        var stored = selectedIn == "Orca231" ? Orca231 : Bambu271;
        var filament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, stored)[stored];

        var result = await ResolveAsync(catalog, filament, pin);

        result.Outcome.Should().Be(ProfileResolutionOutcome.CatalogNotLoaded);
        result.Resolved.Should().BeNull();
        result.Message.Should().Contain($"{(engine == "BambuStudio" ? "Bambu Studio" : engine)} 9.9.9 profiles are not loaded");
    }

    [Fact]
    public async Task EngineWithoutProfilePresets_IsBlockedForEveryRow()
    {
        var catalog = new TwoEngineCatalog();
        var filament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231)[Orca231];
        var user = FilamentProfile.Create("Mine", ProfileSourceFormat.OrcaSlicer, null, "{}");

        (await ResolveAsync(catalog, filament, SuperSlicer)).Outcome.Should().Be(ProfileResolutionOutcome.EngineWithoutProfiles);
        (await ResolveAsync(catalog, user, SuperSlicer)).Outcome.Should().Be(ProfileResolutionOutcome.EngineWithoutProfiles);
    }

    // ---------------------------------------------------------------- imported (User) presets: native only

    [Fact]
    public async Task UserPresetInThePinnedEnginesFormat_IsUsedAsIs()
    {
        var catalog = new TwoEngineCatalog();
        var user = FilamentProfile.Create("My PLA", ProfileSourceFormat.BambuStudio, null, "{}");

        var result = await ResolveAsync(catalog, user, Bambu271);

        result.Outcome.Should().Be(ProfileResolutionOutcome.UserNative);
        result.Resolved.Should().BeSameAs(user);
    }

    [Theory]
    [InlineData(ProfileSourceFormat.OrcaSlicer, "Bambu Studio 2.7.1", "is for OrcaSlicer")]
    [InlineData(ProfileSourceFormat.BambuStudio, "OrcaSlicer 2.3.1", "is for Bambu Studio")]
    public async Task UserPresetOfTheOtherEnginesFormat_IsNeverRun(ProfileSourceFormat format, string pinned, string described)
    {
        var catalog = new TwoEngineCatalog();
        var user = FilamentProfile.Create("My PLA", format, null, "{}");
        var pin = format == ProfileSourceFormat.OrcaSlicer ? Bambu271 : Orca231;

        var result = await ResolveAsync(catalog, user, pin);

        result.Outcome.Should().Be(ProfileResolutionOutcome.UnsupportedOrigin);
        result.Resolved.Should().BeNull();
        result.Message.Should().Contain("'My PLA'").And.Contain(described).And.Contain(pinned);
    }

    [Fact]
    public async Task SystemRowWithoutAVersion_IsInvalid()
    {
        var catalog = new TwoEngineCatalog();
        var filament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", null, Orca231)[Orca231];
        typeof(FilamentProfile).GetProperty(nameof(FilamentProfile.SourceVersion))!.SetValue(filament, null);

        (await ResolveAsync(catalog, filament, Orca231)).Outcome.Should().Be(ProfileResolutionOutcome.InvalidRow);
    }

    // ---------------------------------------------------------------- ancestors

    [Fact]
    public async Task Ancestor_OfAnotherVersion_FollowsItsSettingId_AndNeedNotBeSelectable()
    {
        var catalog = new TwoEngineCatalog();
        var oldBase = FilamentProfile.CreateSystemProfile("PLA @base", ProfileSourceFormat.OrcaSlicer, null, "{}", BambuVendor, null,
            isSelectable: false, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "2.3.1");
        var newBase = FilamentProfile.CreateSystemProfile("PLA @base", ProfileSourceFormat.OrcaSlicer, null, "{}", BambuVendor, null,
            isSelectable: false, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "2.4.2");
        catalog.Filaments.Stage(oldBase);
        catalog.Filaments.Stage(newBase);
        catalog.Loaded(Orca242);

        (await catalog.Resolver.MapAncestorAsync(oldBase, Orca242, default)).Should().BeSameAs(newBase);
        (await catalog.Resolver.MapAncestorAsync(newBase, Orca242, default)).Should().BeSameAs(newBase);
    }

    [Fact]
    public async Task Ancestor_WithNoCounterpart_BlocksTheChainAndSaysWhy()
    {
        var catalog = new TwoEngineCatalog();
        var oldBase = FilamentProfile.CreateSystemProfile("PLA @base", ProfileSourceFormat.OrcaSlicer, null, "{}", BambuVendor, null,
            isSelectable: false, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "2.3.1");
        catalog.Filaments.Stage(oldBase);
        catalog.Loaded(Orca242);

        var act = async () => await catalog.Resolver.MapAncestorAsync(oldBase, Orca242, default);

        var failure = await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
        failure.Which.Code.Should().Be(nameof(ProfileResolutionOutcome.Missing));
        failure.Which.Message.Should().Contain("'PLA @base' is not in OrcaSlicer 2.4.2");
    }

    [Fact]
    public async Task Ancestor_OfAnotherEnginesFormat_IsNeverMapped()
    {
        var catalog = new TwoEngineCatalog();
        var bambuBase = FilamentProfile.CreateSystemProfile("PLA @base", ProfileSourceFormat.BambuStudio, null, "{}", BambuVendor, null,
            isSelectable: false, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "2.7.1");
        catalog.Filaments.Stage(bambuBase);
        catalog.Filament(BambuVendor, "PLA @base", null, null, Orca242);

        var act = async () => await catalog.Resolver.MapAncestorAsync(bambuBase, Orca242, default);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>()).Which.Code.Should().Be(nameof(ProfileResolutionOutcome.UnsupportedOrigin));
    }
}
