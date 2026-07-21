using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Tests.Parts.Fakes;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Parts;

// TDD (write-first) — this resolution algorithm is a pure, deterministic function over already-
// persisted rows, the same bar the constitution sets for ResolveSlicerSelection.
public class ResolveSlicingProfileSelectionTests
{
    private static ResolveSlicingProfileSelection CreateSelection(
        Organization organization,
        FakeMachineProfileRepository machineRepo,
        FakeProcessProfileRepository processRepo,
        FakeFilamentProfileRepository filamentRepo,
        FakeOrganizationFilamentDefaultRepository filamentDefaultRepo) =>
        new(new FakeOrganizationRepository(organization), machineRepo, processRepo, filamentRepo, filamentDefaultRepo);

    [Fact]
    public async Task ResolveAsync_EverythingConfigured_ReturnsAllThreeMergedJson()
    {
        var organization = Organization.Create("Test Org");
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        var filamentDefaultRepo = new FakeOrganizationFilamentDefaultRepository();

        var machine = MachineProfile.Create(organization.Id, "My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        organization.SetDefaultMachineProfile(machine.Id);

        var process = ProcessProfile.Create(organization.Id, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""");
        await processRepo.AddAsync(process, CancellationToken.None);
        machine.SetDefaultProcessProfile(process.Id);

        var filament = FilamentProfile.Create(organization.Id, "Generic PLA", ProfileSourceFormat.OrcaSlicer, null, """{"type":"filament","filament_type":["PLA"]}""");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await filamentDefaultRepo.AddAsync(OrganizationFilamentDefault.Create(organization.Id, "PLA", filament.Id), CancellationToken.None);

        var selection = CreateSelection(organization, machineRepo, processRepo, filamentRepo, filamentDefaultRepo);

        var result = await selection.ResolveAsync("PLA", CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("\"nozzle_diameter\"");
        result.ProcessSettingsJson.Should().Contain("\"layer_height\"");
        result.FilamentSettingsJson.Should().Contain("\"filament_type\"");
    }

    [Fact]
    public async Task ResolveAsync_NoDefaultMachineProfile_ThrowsSlicingProfileUnconfigured()
    {
        var organization = Organization.Create("Test Org");
        var selection = CreateSelection(
            organization, new FakeMachineProfileRepository(), new FakeProcessProfileRepository(),
            new FakeFilamentProfileRepository(), new FakeOrganizationFilamentDefaultRepository());

        var act = async () => await selection.ResolveAsync("PLA", CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_DefaultMachineProfileRowMissing_ThrowsSlicingProfileUnconfigured()
    {
        var organization = Organization.Create("Test Org");
        organization.SetDefaultMachineProfile(Guid.NewGuid());
        var selection = CreateSelection(
            organization, new FakeMachineProfileRepository(), new FakeProcessProfileRepository(),
            new FakeFilamentProfileRepository(), new FakeOrganizationFilamentDefaultRepository());

        var act = async () => await selection.ResolveAsync("PLA", CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_MachineHasNoPairedProcessProfile_ThrowsSlicingProfileUnconfigured()
    {
        var organization = Organization.Create("Test Org");
        var machineRepo = new FakeMachineProfileRepository();
        var machine = MachineProfile.Create(organization.Id, "My Printer", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        organization.SetDefaultMachineProfile(machine.Id);

        var selection = CreateSelection(
            organization, machineRepo, new FakeProcessProfileRepository(),
            new FakeFilamentProfileRepository(), new FakeOrganizationFilamentDefaultRepository());

        var act = async () => await selection.ResolveAsync("PLA", CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_NoDefaultFilamentForMaterial_ThrowsSlicingProfileUnconfigured()
    {
        var organization = Organization.Create("Test Org");
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();

        var machine = MachineProfile.Create(organization.Id, "My Printer", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        organization.SetDefaultMachineProfile(machine.Id);

        var process = ProcessProfile.Create(organization.Id, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await processRepo.AddAsync(process, CancellationToken.None);
        machine.SetDefaultProcessProfile(process.Id);

        var selection = CreateSelection(
            organization, machineRepo, processRepo, new FakeFilamentProfileRepository(), new FakeOrganizationFilamentDefaultRepository());

        var act = async () => await selection.ResolveAsync("PETG", CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_MachineProfileHasUnresolvedAncestorChain_ThrowsSlicingProfileUnconfigured()
    {
        var organization = Organization.Create("Test Org");
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        var filamentDefaultRepo = new FakeOrganizationFilamentDefaultRepository();

        // Declares a base name that was never resolved to an id — an unresolved chain link.
        var machine = MachineProfile.Create(organization.Id, "My Printer", ProfileSourceFormat.OrcaSlicer, "Some Missing Base", "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        organization.SetDefaultMachineProfile(machine.Id);

        var process = ProcessProfile.Create(organization.Id, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await processRepo.AddAsync(process, CancellationToken.None);
        machine.SetDefaultProcessProfile(process.Id);

        var filament = FilamentProfile.Create(organization.Id, "Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await filamentDefaultRepo.AddAsync(OrganizationFilamentDefault.Create(organization.Id, "PLA", filament.Id), CancellationToken.None);

        var selection = CreateSelection(organization, machineRepo, processRepo, filamentRepo, filamentDefaultRepo);

        var act = async () => await selection.ResolveAsync("PLA", CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }
}
