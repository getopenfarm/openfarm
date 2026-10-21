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
public class GetFilamentProfilesQueryHandlerTests
{
    private static GetFilamentProfilesQueryHandler CreateHandler(FakeFilamentProfileRepository filamentRepo) =>
        new(filamentRepo, new StubProfileBundleParser(), new ProfileCompatibility(new FakeMachineProfileRepository(), filamentRepo), new FakeSlicingDefaultsQuery());

    [Fact]
    public async Task HandleAsync_LegacyBrowseCall_ExcludesSystemRows()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var userRow = FilamentProfile.Create("My Tuned PLA", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var systemRow = FilamentProfile.CreateSystemProfile(
            "Bambu PLA Basic @BBL X1C", ProfileSourceFormat.BambuStudio, null, "{}",
            "Bambulab", "GFA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(userRow, CancellationToken.None);
        await filamentRepo.AddAsync(systemRow, CancellationToken.None);

        var handler = CreateHandler(filamentRepo);

        var page = await handler.HandleAsync(new GetFilamentProfilesQuery(), CancellationToken.None);

        page.Items.Should().ContainSingle();
        page.Items[0].Id.Should().Be(userRow.Id);
        page.Items[0].Origin.Should().Be(ProfileOrigin.User);
    }

    [Fact]
    public async Task HandleAsync_PickerStyleCallWithOriginAll_StillReturnsSystemRows()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var systemRow = FilamentProfile.CreateSystemProfile(
            "Bambu PLA Basic @BBL X1C", ProfileSourceFormat.BambuStudio, null, "{}",
            "Bambulab", "GFA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(systemRow, CancellationToken.None);

        var handler = CreateHandler(filamentRepo);

        var page = await handler.HandleAsync(new GetFilamentProfilesQuery(Origin: "All"), CancellationToken.None);

        page.Items.Should().ContainSingle(i => i.Id == systemRow.Id);
    }

    [Fact]
    public async Task HandleAsync_TwoIngestedVersions_PickerListsOnlyTheCatalogVersion()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        FilamentProfile Row(string version) => FilamentProfile.CreateSystemProfile(
            "Bambu PLA Basic @BBL X1C", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GFA00",
            isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: version);
        var orca231 = Row("2.3.1");
        var orca242 = Row("2.4.2");
        await filamentRepo.AddAsync(orca231, CancellationToken.None);
        await filamentRepo.AddAsync(orca242, CancellationToken.None);
        // Version resolution reads the machine catalog's ingested versions.
        await machineRepo.AddAsync(MachineProfile.CreateSystemProfile(
            "Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GM014",
            isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "2.4.2"), CancellationToken.None);
        var versions = CatalogVersionsFactory.Create(machineRepo, new SlicerRef(SlicerEngine.OrcaSlicer, "2.4.2"),
            CatalogVersionsFactory.Bundled(SlicerEngine.OrcaSlicer, "2.4.2"));
        var handler = new GetFilamentProfilesQueryHandler(filamentRepo, new StubProfileBundleParser(),
            new ProfileCompatibility(machineRepo, filamentRepo), new FakeSlicingDefaultsQuery(), versions);

        var page = await handler.HandleAsync(new GetFilamentProfilesQuery(Origin: "All"), CancellationToken.None);

        page.Items.Should().ContainSingle(i => i.Id == orca242.Id);
        page.Items.Should().NotContain(i => i.Id == orca231.Id);
        page.Items.Single().SourceVersion.Should().Be("2.4.2");
    }

    private sealed class StubProfileBundleParser : IProfileBundleParser
    {
        public Task<ParsedProfileBundle> ParseAsync(Stream bundleStream, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public MachineProfileDisplayFields ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) =>
            throw new NotSupportedException();

        public FilamentProfileDisplayFields ExtractFilamentDisplayFields(
            string rawDataJson, ProfileSourceFormat sourceFormat, string name, bool hasBaseProfileName) =>
            new(Material: null, Brand: null, ColorHex: null, NozzleSizeMm: null, NozzleTempC: null,
                BedTempC: null, MaxVolumetricSpeedMm3s: null, FanPercent: null, CompatiblePrinterNames: []);

        public ProcessProfileDisplayFields ExtractProcessDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) =>
            throw new NotSupportedException();
    }
}
