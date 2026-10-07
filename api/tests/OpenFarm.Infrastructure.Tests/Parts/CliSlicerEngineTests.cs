using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SliceConfiguredAsync_PreservesExportedFileBytesAndFormat(bool project)
    {
        if (OperatingSystem.IsWindows()) return;
        var invocation = new ExportFixtureInvocation(project);
        var engine = new CliSlicerEngine(
            new FakeAvailableSlicerCatalog(new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/bin/sh")),
            new FakeHostPlatformProvider(SlicerPlatform.Linux), [invocation], CreateProfileSelection(),
            Options.Create(new SlicerOptions()), NullLogger<CliSlicerEngine>.Instance);
        using var input = new MemoryStream([1, 2, 3]);

        var result = await engine.SliceConfiguredAsync(Orca220, input, PartFileFormat.Stl, "PLA", 0.4m, Guid.NewGuid(),
            new ResolvedSlicingProfiles("{}", "{}", "{}", "Machine", "Process", "Filament"), default);

        result.OutputFileBytes.Should().Equal(invocation.ExportedBytes);
        result.OutputFileFormat.Should().Be(project ? PartFileFormat.ThreeMf : PartFileFormat.GCode);
        result.PlateCount.Should().Be(1);
    }

    private sealed class ExportFixtureInvocation(bool project) : ISlicerInvocation
    {
        public byte[] ExportedBytes { get; private set; } = [];
        public bool SupportsEngine(SlicerEngine engine) => true;

        public SlicerProcessInvocation BuildInvocation(AvailableSlicer slicer, string modelFilePath, string materialType,
            decimal nozzleDiameterMm, string workDirectoryPath, ResolvedSlicingProfiles? resolvedProfiles)
        {
            var output = Path.Combine(workDirectoryPath, project ? "output.gcode.3mf" : "output.gcode");
            const string gcode = "; nozzle_diameter = 0.4\n; filament_type = PLA\nG28\n";
            if (project)
            {
                using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
                using (var plate = new StreamWriter(archive.CreateEntry("Metadata/plate_1.gcode").Open())) plate.Write(gcode);
                using var settings = new StreamWriter(archive.CreateEntry("Metadata/project_settings.config").Open());
                settings.Write("{}");
            }
            else
            {
                File.WriteAllText(output, gcode);
            }
            ExportedBytes = File.ReadAllBytes(output);
            return new(slicer.ExecutablePath, "-c true", output, project);
        }
    }

    [Fact]
    public async Task SliceAsync_SlicerNotInCatalog_ThrowsSlicerUnavailableException()
    {
        var engine = CreateEngine(new FakeAvailableSlicerCatalog());
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.SliceAsync(Orca220, modelStream, PartFileFormat.Stl, "Generic PLA", 0.4m, printerId: null, CancellationToken.None);

        await act.Should().ThrowAsync<SlicerUnavailableException>();
    }

    [Fact]
    public async Task EstimateAsync_BundledExecutableDoesNotExist_ThrowsSlicingFailedException()
    {
        var catalog = new FakeAvailableSlicerCatalog(
            new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/path/does/not/exist/orca-slicer-cli"));
        var engine = CreateEngine(catalog);
        using var modelStream = new MemoryStream([1, 2, 3]);

        var act = async () => await engine.EstimateAsync(Orca220, modelStream, PartFileFormat.ThreeMf,
            new EstimationConfiguration(true, null, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingFailedException>())
            .WithMessage("*Could not start the OrcaSlicer CLI*");
    }

    [Fact]
    public async Task EstimateAsync_RelativeExecutablePath_IsResolvedBeforeChangingWorkingDirectory()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("openfarm-relative-slicer-");
        try
        {
            var path = Path.Combine(directory.FullName, "AppRun");
            await File.WriteAllTextAsync(path, "#!/bin/sh\nexit 1\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var catalog = new FakeAvailableSlicerCatalog(new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux,
                Path.GetRelativePath(Environment.CurrentDirectory, path)));
            var engine = CreateEngine(catalog);
            using var input = new MemoryStream([1, 2, 3]);
            var act = async () => await engine.EstimateAsync(Orca220, input, PartFileFormat.ThreeMf,
                new EstimationConfiguration(true, null, null, null), CancellationToken.None);
            await act.Should().ThrowAsync<SlicingFailedException>().WithMessage("*exited with code 1*");
        }
        finally { directory.Delete(true); }
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
        Options.Create(new SlicerOptions()),
        NullLogger<CliSlicerEngine>.Instance);

    // A fully configured (machine, process, filament) triple for a printer with material
    // "Generic PLA" — these tests only exercise failure paths that occur before or after profile
    // resolution, so resolution itself must succeed rather than mask the behavior under test with
    // an unrelated unconfigured error. Material resolution keys off FilamentProfile.MaterialType
    // (not the profile's Name), so MaterialType is set to the same "Generic PLA" string the tests
    // pass as SliceAsync's materialType argument.
    private static ResolveSlicingProfileSelection CreateProfileSelection()
    {
        var organizationId = Guid.NewGuid();
        var machine = MachineProfile.Create(organizationId, "Test Machine", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var process = ProcessProfile.CreateSystemProfile(organizationId, "Test Process", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: null);
        var filament = FilamentProfile.CreateSystemProfile(
            organizationId, "Generic PLA", ProfileSourceFormat.OrcaSlicer, null, """{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"]}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "Generic PLA", includedTemplateNames: null);

        var printer = Printer.Create(organizationId, "Test Printer", "Bambu", "X1C", "10.0.0.5", "SN-TEST");
        printer.LinkMachineProfile(machine.Id);
        printer.LinkProcessProfile(process.Id);
        // 012 — this fixture's own printer-tied resolution must succeed to exercise the unrelated
        // failure paths these tests actually cover; X1C's catalog compatible set includes HighTemp.
        printer.SelectBuildPlate(BuildPlateType.HighTemp);

        return new ResolveSlicingProfileSelection(
            new FakePrinterRepository(printer),
            new FakeMachineProfileRepository(machine),
            new FakeProcessProfileRepository(process),
            new FakeFilamentProfileRepository(filament),
            new RepresentativeFilamentResolver(
                new FakeFilamentProfileRepository(filament), new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository(), new FakePrinterRepository(printer), new FakeMachineProfileRepository(machine), new ProfileCompatibility(new FakeMachineProfileRepository(machine), new FakeFilamentProfileRepository(filament))),
            new ProfileCompatibility(new FakeMachineProfileRepository(machine), new FakeFilamentProfileRepository(filament)));
    }

    private sealed class FakePrinterRepository(params Printer[] rows) : IPrinterRepository
    {
        public Task AddAsync(Printer printer, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.Id == id));

        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.SerialNumber == serialNumber));

        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Printer>>(rows);

        public Task RemoveAsync(Printer printer, CancellationToken cancellationToken) => Task.CompletedTask;

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

        public Task<MachineProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.SourceFormat == sourceFormat && p.VendorName == vendorName && p.ExternalId == externalId));

        public Task<MachineProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.SourceFormat == sourceFormat && p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

        public Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
            string? searchText, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by CliSlicerEngineTests.");

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

        public Task<ProcessProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.SourceFormat == sourceFormat && p.VendorName == vendorName && p.ExternalId == externalId));

        public Task<ProcessProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.SourceFormat == sourceFormat && p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

        public Task<(IReadOnlyList<ProcessProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
            string? searchText, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by CliSlicerEngineTests.");

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

        public Task<FilamentProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.SourceFormat == sourceFormat && p.VendorName == vendorName && p.ExternalId == externalId));

        public Task<FilamentProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult(rows.FirstOrDefault(p => p.SourceFormat == sourceFormat && p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

        public Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
            string? searchText, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by CliSlicerEngineTests.");

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Always-empty — these tests only exercise the System-catalog tier (FakeFilamentProfileRepository
    // above); none of them register a spool, so the fleet-loaded/library tiers never find candidates.
    private sealed class FakeSpoolRepository : ISpoolRepository
    {
        public Task AddAsync(Spool spool, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Spool?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Spool?>(null);

        public Task<IReadOnlyList<Spool>> ListAsync(SpoolState? state, Guid? filamentVariantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Spool>>([]);

        public Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Spool>>([]);

        public Task<Spool?> FindUsingFeedSlotAsync(Guid feedSlotId, CancellationToken cancellationToken) => Task.FromResult<Spool?>(null);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFilamentRepository : IFilamentRepository
    {
        public Task AddAsync(Filament filament, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Filament?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Filament?>(null);

        public Task<IReadOnlyList<Filament>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Filament>>([]);

        public Task<FilamentVariant?> FindVariantByIdAsync(Guid filamentVariantId, CancellationToken cancellationToken) =>
            Task.FromResult<FilamentVariant?>(null);

        public Task AddVariantAsync(FilamentVariant variant, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFeedUnitRepository : IFeedUnitRepository
    {
        public Task AddAsync(FeedUnit feedUnit, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<FeedUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<FeedUnit?>(null);

        public Task<IReadOnlyList<FeedUnit>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FeedUnit>>([]);

        public Task<IReadOnlyList<FeedUnit>> GetByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeedUnit>>([]);

        public Task<FeedUnit?> FindByPrinterKindUnitIndexAsync(Guid printerId, string kind, int unitIndex, CancellationToken cancellationToken) =>
            Task.FromResult<FeedUnit?>(null);

        public Task<FeedSlot?> GetFeedSlotByIdAsync(Guid feedSlotId, CancellationToken cancellationToken) => Task.FromResult<FeedSlot?>(null);

        public Task<FeedUnit?> GetByFeedSlotIdAsync(Guid feedSlotId, CancellationToken cancellationToken) => Task.FromResult<FeedUnit?>(null);

        public void TrackNewFeedSlot(FeedSlot feedSlot)
        {
        }

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
