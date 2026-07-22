using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

public class GetMachineProfileQueryHandlerTests
{
    private static GetMachineProfileQueryHandler CreateHandler(FakeMachineProfileRepository machineRepo) =>
        new(machineRepo, new StubProfileBundleParser());

    [Fact]
    public async Task HandleAsync_UserImportedRow_ReturnsUserOriginAndNoVendorName()
    {
        var machineRepo = new FakeMachineProfileRepository();

        var machine = MachineProfile.Create(Guid.NewGuid(), "Test Machine", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var handler = CreateHandler(machineRepo);

        var detail = await handler.HandleAsync(new GetMachineProfileQuery(machine.Id), CancellationToken.None);

        detail!.Origin.Should().Be(ProfileOrigin.User);
        detail.VendorName.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_SystemIngestedRow_ReturnsSystemOriginAndVendorName()
    {
        var machineRepo = new FakeMachineProfileRepository();

        var machine = MachineProfile.CreateSystemProfile(
            Guid.NewGuid(), "Bambu Lab X1 Carbon 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, "{}",
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: "Bambu Lab X1 Carbon", includedTemplateNames: null);
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var handler = CreateHandler(machineRepo);

        var detail = await handler.HandleAsync(new GetMachineProfileQuery(machine.Id), CancellationToken.None);

        detail!.Origin.Should().Be(ProfileOrigin.System);
        detail.VendorName.Should().Be("Bambulab");
    }

    [Fact]
    public async Task HandleAsync_UnknownId_ReturnsNull()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var handler = CreateHandler(machineRepo);

        var detail = await handler.HandleAsync(new GetMachineProfileQuery(Guid.NewGuid()), CancellationToken.None);

        detail.Should().BeNull();
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
