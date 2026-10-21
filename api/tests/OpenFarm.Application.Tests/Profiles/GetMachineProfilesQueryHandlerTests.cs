using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

// Regression coverage for a real bug found via live-DB verification: the legacy browse-page path
// (feature 003, called with every field null/omitted) once returned every origin — including
// System rows ingested by 009 — violating FR-013's "browse/import page is User-only" rule. The
// picker path (any field set) has always filtered by origin explicitly and is unaffected.
public class GetMachineProfilesQueryHandlerTests
{
    private static GetMachineProfilesQueryHandler CreateHandler(FakeMachineProfileRepository machineRepo) =>
        new(machineRepo, new StubProfileBundleParser(), new FakePrinterRepository(), new ProfileCompatibility(machineRepo, new FakeFilamentProfileRepository()));

    [Fact]
    public async Task HandleAsync_LegacyBrowseCall_ExcludesSystemRows()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var userRow = MachineProfile.Create("My Tuned X1C", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var systemRow = MachineProfile.CreateSystemProfile(
            "Bambu Lab X1 Carbon 0.4 nozzle", ProfileSourceFormat.BambuStudio, null, "{}",
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "1.0.0");
        await machineRepo.AddAsync(userRow, CancellationToken.None);
        await machineRepo.AddAsync(systemRow, CancellationToken.None);

        var handler = CreateHandler(machineRepo);

        var page = await handler.HandleAsync(new GetMachineProfilesQuery(), CancellationToken.None);

        page.Items.Should().ContainSingle();
        page.Items[0].Id.Should().Be(userRow.Id);
        page.Items[0].Origin.Should().Be(ProfileOrigin.User);
    }

    [Fact]
    public async Task HandleAsync_PickerStyleCallWithOriginAll_StillReturnsSystemRows()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var systemRow = MachineProfile.CreateSystemProfile(
            "Bambu Lab X1 Carbon 0.4 nozzle", ProfileSourceFormat.BambuStudio, null, "{}",
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "1.0.0");
        await machineRepo.AddAsync(systemRow, CancellationToken.None);

        var handler = CreateHandler(machineRepo);

        var page = await handler.HandleAsync(new GetMachineProfilesQuery(Origin: "All"), CancellationToken.None);

        page.Items.Should().ContainSingle(i => i.Id == systemRow.Id);
    }

    [Fact]
    public async Task HandleAsync_TwoIngestedVersions_PickerListsOnlyTheDefaultEnginesCatalogVersion()
    {
        var machineRepo = new FakeMachineProfileRepository();
        MachineProfile Row(ProfileSourceFormat format, string version) => MachineProfile.CreateSystemProfile(
            "Bambu Lab P1S 0.4 nozzle", format, null, "{}", "Bambulab", "GM014",
            isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: version);
        var orca231 = Row(ProfileSourceFormat.OrcaSlicer, "2.3.1");
        var orca242 = Row(ProfileSourceFormat.OrcaSlicer, "2.4.2");
        var bambu271 = Row(ProfileSourceFormat.BambuStudio, "2.7.1");
        foreach (var row in new[] { orca231, orca242, bambu271 })
        {
            await machineRepo.AddAsync(row, CancellationToken.None);
        }

        var versions = CatalogVersionsFactory.Create(machineRepo, new SlicerRef(SlicerEngine.OrcaSlicer, "2.4.2"),
            CatalogVersionsFactory.Bundled(SlicerEngine.OrcaSlicer, "2.3.1"), CatalogVersionsFactory.Bundled(SlicerEngine.OrcaSlicer, "2.4.2"));
        var handler = new GetMachineProfilesQueryHandler(machineRepo, new StubProfileBundleParser(), new FakePrinterRepository(),
            new ProfileCompatibility(machineRepo, new FakeFilamentProfileRepository()), versions);

        var page = await handler.HandleAsync(new GetMachineProfilesQuery(Origin: "All"), CancellationToken.None);

        // One row per logical machine: only the default engine's catalog version is listed; the 2.3.1 catalog and the
        // other engine's twin are hidden (a part pinned to the other engine reaches its own twin through resolution).
        page.Items.Select(i => i.Id).Should().BeEquivalentTo([orca242.Id]);
        page.Items.Single(i => i.Id == orca242.Id).SourceVersion.Should().Be("2.4.2");
    }

    // Production only slices with the pinned slicer's own presets, so the farm default engine's picker offers only its
    // own format — imported presets included — while the page that manages imports still shows every format.
    [Fact]
    public async Task HandleAsync_PickerListsOnlyTheDefaultEnginesFormat_ButTheImportsPageStillListsEveryFormat()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var orcaUser = MachineProfile.Create("My Orca P1S", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var bambuUser = MachineProfile.Create("My Bambu P1S", ProfileSourceFormat.BambuStudio, null, "{}");
        var orcaSystem = MachineProfile.CreateSystemProfile("Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, "{}",
            "Bambulab", "GM014", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "2.4.2");
        var bambuSystem = MachineProfile.CreateSystemProfile("Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.BambuStudio, null, "{}",
            "Bambulab", "GM014", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "2.7.1");
        var bambuOnlySystem = MachineProfile.CreateSystemProfile("Bambu Lab H2D 0.4 nozzle", ProfileSourceFormat.BambuStudio, null, "{}",
            "Bambulab", "GM099", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "2.7.1");
        foreach (var row in new[] { orcaUser, bambuUser, orcaSystem, bambuSystem, bambuOnlySystem })
        {
            await machineRepo.AddAsync(row, CancellationToken.None);
        }

        var versions = CatalogVersionsFactory.Create(machineRepo, new SlicerRef(SlicerEngine.OrcaSlicer, "2.4.2"),
            CatalogVersionsFactory.Bundled(SlicerEngine.OrcaSlicer, "2.4.2"), CatalogVersionsFactory.Bundled(SlicerEngine.BambuStudio, "2.7.1"));
        var handler = new GetMachineProfilesQueryHandler(machineRepo, new StubProfileBundleParser(), new FakePrinterRepository(),
            new ProfileCompatibility(machineRepo, new FakeFilamentProfileRepository()), versions);

        var picker = await handler.HandleAsync(new GetMachineProfilesQuery(Origin: "All"), CancellationToken.None);
        var imports = await handler.HandleAsync(new GetMachineProfilesQuery(), CancellationToken.None);

        picker.Items.Select(i => i.Id).Should().BeEquivalentTo([orcaUser.Id, orcaSystem.Id]);
        imports.Items.Select(i => i.Id).Should().BeEquivalentTo([orcaUser.Id, bambuUser.Id]);
    }

    private sealed class StubProfileBundleParser : IProfileBundleParser
    {
        public Task<ParsedProfileBundle> ParseAsync(Stream bundleStream, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public MachineProfileDisplayFields ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) =>
            new(NozzleSizeMm: null, BedSizeXMm: null, BedSizeYMm: null, MaxHeightMm: null, HasEnclosure: null, SupportedMaterials: []);

        public FilamentProfileDisplayFields ExtractFilamentDisplayFields(
            string rawDataJson, ProfileSourceFormat sourceFormat, string name, bool hasBaseProfileName) =>
            throw new NotSupportedException();

        public ProcessProfileDisplayFields ExtractProcessDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) =>
            throw new NotSupportedException();
    }
}
