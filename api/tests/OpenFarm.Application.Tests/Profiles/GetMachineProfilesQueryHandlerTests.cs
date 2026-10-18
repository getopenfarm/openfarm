using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
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
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null);
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
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null);
        await machineRepo.AddAsync(systemRow, CancellationToken.None);

        var handler = CreateHandler(machineRepo);

        var page = await handler.HandleAsync(new GetMachineProfilesQuery(Origin: "All"), CancellationToken.None);

        page.Items.Should().ContainSingle(i => i.Id == systemRow.Id);
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
