using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Regression coverage for the bug reported against a real (non-Docker) dev machine: a missing/broken
// CLI binary must surface as SlicingFailedException (so RunEstimateContinuationAsync can mark the
// job/part failed) rather than an uncaught Win32Exception that the background service's generic
// catch silently swallows, leaving the job stuck in "Resolving" forever with no user-facing error.
// Also covers the Phase 8 catalog-driven resolution path: an (engine, version) absent from the
// catalog throws SlicerUnavailableException instead of ever starting a process.
public class CliSlicerEngineTests
{
    private static readonly SlicerRef Orca220 = new(SlicerEngine.OrcaSlicer, "2.2.0");

    [Fact]
    public async Task SliceAsync_SlicerNotInCatalog_ThrowsSlicerUnavailableException()
    {
        var engine = CreateEngine(new FakeAvailableSlicerCatalog());
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceAsync(Orca220, modelStream, PartFileFormat.Stl, "Generic PLA", 0.4m, CancellationToken.None);

        await act.Should().ThrowAsync<SlicerUnavailableException>();
    }

    [Fact]
    public async Task SliceAsync_BundledExecutableDoesNotExist_ThrowsSlicingFailedException()
    {
        var catalog = new FakeAvailableSlicerCatalog(
            new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/path/does/not/exist/orca-slicer-cli"));
        var engine = CreateEngine(catalog);
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceAsync(Orca220, modelStream, PartFileFormat.Stl, "Generic PLA", 0.4m, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingFailedException>())
            .WithMessage("*Could not start the OrcaSlicer CLI*");
    }

    [Theory]
    [InlineData(PartFileFormat.Stl, "model.stl")]
    [InlineData(PartFileFormat.ThreeMf, "model.3mf")]
    public void ResolveInputFileName_UsesTheRealExtensionSoTheSlicerDoesNotMisdetectTheFormat(PartFileFormat format, string expected)
    {
        CliSlicerEngine.ResolveInputFileName(format).Should().Be(expected);
    }

    [Fact]
    public async Task ReadEmbeddedPlateGCodeAsync_ThreeMfWithPlateOneGCode_ReturnsItsContent()
    {
        var threeMfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.gcode.3mf");
        using (var archive = ZipFile.Open(threeMfPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Metadata/plate_1.gcode");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("; nozzle_diameter = 0.4\nG28\n");
        }

        try
        {
            var gcode = await CliSlicerEngine.ReadEmbeddedPlateGCodeAsync(threeMfPath, CancellationToken.None);

            gcode.Should().Contain("nozzle_diameter = 0.4");
        }
        finally
        {
            File.Delete(threeMfPath);
        }
    }

    [Fact]
    public async Task ReadEmbeddedPlateGCodeAsync_ThreeMfMissingPlateOneGCode_ThrowsSlicingFailedException()
    {
        var threeMfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.gcode.3mf");
        using (ZipFile.Open(threeMfPath, ZipArchiveMode.Create))
        {
            // Deliberately empty — no Metadata/plate_1.gcode entry.
        }

        try
        {
            var act = async () => await CliSlicerEngine.ReadEmbeddedPlateGCodeAsync(threeMfPath, CancellationToken.None);

            await act.Should().ThrowAsync<SlicingFailedException>();
        }
        finally
        {
            File.Delete(threeMfPath);
        }
    }

    private static CliSlicerEngine CreateEngine(FakeAvailableSlicerCatalog catalog) => new(
        catalog,
        new FakeHostPlatformProvider(SlicerPlatform.Linux),
        [new PrusaSliceFamilyInvocation(), new SuperSlicerInvocation()],
        CreateProfileSelection(),
        Options.Create(new SlicerOptions()));

    // A fully configured (machine, process, filament) triple for "Generic PLA" — these tests only
    // exercise failure paths that occur before or after profile resolution, so resolution itself
    // must succeed rather than mask the behavior under test with an unrelated unconfigured error.
    private static ResolveSlicingProfileSelection CreateProfileSelection()
    {
        var organization = Organization.Create("Test Org");
        var machine = MachineProfile.Create(organization.Id, "Test Machine", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var process = ProcessProfile.Create(organization.Id, "Test Process", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var filament = FilamentProfile.Create(organization.Id, "Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}");
        machine.SetDefaultProcessProfile(process.Id);
        organization.SetDefaultMachineProfile(machine.Id);
        var filamentDefault = OrganizationFilamentDefault.Create(organization.Id, "Generic PLA", filament.Id);

        return new ResolveSlicingProfileSelection(
            new FakeOrganizationRepository(organization),
            new FakeMachineProfileRepository(machine),
            new FakeProcessProfileRepository(process),
            new FakeFilamentProfileRepository(filament),
            new FakeOrganizationFilamentDefaultRepository(filamentDefault));
    }

    private sealed class FakeOrganizationRepository(Organization organization) : IOrganizationRepository
    {
        public Task<Organization> GetAsync(CancellationToken cancellationToken) => Task.FromResult(organization);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMachineProfileRepository(params MachineProfile[] rows) : IMachineProfileRepository
    {
        public Task AddAsync(MachineProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(rows);

        public Task<IReadOnlyList<MachineProfile>> FindByNameAsync(ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeProcessProfileRepository(params ProcessProfile[] rows) : IProcessProfileRepository
    {
        public Task AddAsync(ProcessProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(rows);

        public Task<IReadOnlyList<ProcessProfile>> FindByNameAsync(ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFilamentProfileRepository(params FilamentProfile[] rows) : IFilamentProfileRepository
    {
        public Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(rows);

        public Task<IReadOnlyList<FilamentProfile>> FindByNameAsync(ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeOrganizationFilamentDefaultRepository(params OrganizationFilamentDefault[] rows) : IOrganizationFilamentDefaultRepository
    {
        public Task AddAsync(OrganizationFilamentDefault entry, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<OrganizationFilamentDefault?> FindByMaterialTypeAsync(string materialType, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(d => d.MaterialType == materialType));

        public Task<IReadOnlyList<OrganizationFilamentDefault>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrganizationFilamentDefault>>(rows);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeAvailableSlicerCatalog(params AvailableSlicer[] available) : IAvailableSlicerCatalog
    {
        public IReadOnlyList<AvailableSlicer> GetAvailable() => available;

        public AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform) =>
            available.FirstOrDefault(a =>
                a.Engine == slicerRef.Engine
                && string.Equals(a.Version, slicerRef.Version, StringComparison.Ordinal)
                && a.Platform == platform);
    }

    private sealed class FakeHostPlatformProvider(SlicerPlatform current) : IHostPlatformProvider
    {
        public SlicerPlatform Current { get; } = current;
    }
}
