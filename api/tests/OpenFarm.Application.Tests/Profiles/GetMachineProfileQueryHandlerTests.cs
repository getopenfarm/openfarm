using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

public class GetMachineProfileQueryHandlerTests
{
    private static GetMachineProfileQueryHandler CreateHandler(
        FakeMachineProfileRepository machineRepo, FakeProcessProfileRepository processRepo) =>
        new(machineRepo, processRepo, new StubProfileBundleParser());

    [Fact]
    public async Task HandleAsync_MachineHasPairedProcessProfile_ReturnsItsIdAndName()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();

        var machine = MachineProfile.Create(Guid.NewGuid(), "Test Machine", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var process = ProcessProfile.Create(machine.OrganizationId, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}");
        machine.SetDefaultProcessProfile(process.Id);
        await machineRepo.AddAsync(machine, CancellationToken.None);
        await processRepo.AddAsync(process, CancellationToken.None);

        var handler = CreateHandler(machineRepo, processRepo);

        var detail = await handler.HandleAsync(new GetMachineProfileQuery(machine.Id), CancellationToken.None);

        detail!.DefaultProcessProfileId.Should().Be(process.Id);
        detail.DefaultProcessProfileName.Should().Be("0.20mm Standard");
    }

    [Fact]
    public async Task HandleAsync_PairedProcessProfileRowIsMissing_DegradesToNullRatherThanThrowing()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();

        var machine = MachineProfile.Create(Guid.NewGuid(), "Test Machine", ProfileSourceFormat.OrcaSlicer, null, "{}");
        machine.SetDefaultProcessProfile(Guid.NewGuid());
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var handler = CreateHandler(machineRepo, processRepo);

        var detail = await handler.HandleAsync(new GetMachineProfileQuery(machine.Id), CancellationToken.None);

        detail!.DefaultProcessProfileId.Should().Be(machine.DefaultProcessProfileId);
        detail.DefaultProcessProfileName.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_MachineHasNoPairedProcessProfile_BothAreNull()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var machine = MachineProfile.Create(Guid.NewGuid(), "Test Machine", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var handler = CreateHandler(machineRepo, new FakeProcessProfileRepository());

        var detail = await handler.HandleAsync(new GetMachineProfileQuery(machine.Id), CancellationToken.None);

        detail!.DefaultProcessProfileId.Should().BeNull();
        detail.DefaultProcessProfileName.Should().BeNull();
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
