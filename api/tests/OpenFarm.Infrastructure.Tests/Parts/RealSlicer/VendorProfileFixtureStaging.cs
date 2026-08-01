using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Profiles;

namespace OpenFarm.Infrastructure.Tests.Parts.RealSlicer;

internal sealed record StagedProfileSelection(ResolveSlicingProfileSelection Resolver, Guid PrinterId, Guid FilamentProfileId);

/// <summary>
/// Runs the real <see cref="SystemProfileIngestionService"/> directly against the real, locally
/// installed slicer bundle's own <c>resources/profiles/</c> (found by <see cref="RealSlicerBundleLocator.GetLeafDirectory"/>)
/// into in-memory fakes — the actual, complete, slice-ready vendor presets a real install ships,
/// not the small hand-curated fixtures under Profiles/Fixtures/VendorIndex (those exist only to
/// unit-test the ingestion parser's field-mapping in isolation and are not slice-ready). No
/// Postgres/Testcontainers needed since ingestion only reads from the bundle and writes to fakes.
/// </summary>
internal static class VendorProfileFixtureStaging
{
    public static async Task<StagedProfileSelection> StageAndIngestAsync(
        SlicerEngine engine, string version, string machineProfileName, string processProfileName, string filamentProfileName,
        CancellationToken cancellationToken)
    {
        var leaf = RealSlicerBundleLocator.GetLeafDirectory(engine, version);

        var machineRepo = new MutableMachineProfileRepository();
        var processRepo = new MutableProcessProfileRepository();
        var filamentRepo = new MutableFilamentProfileRepository();
        var vendorRepo = new MutableVendorRepository();
        var printerRepo = new MutablePrinterRepository();

        var ingestionService = new SystemProfileIngestionService(
            machineRepo, processRepo, filamentRepo, vendorRepo,
            new VendorProfileIndexReader(NullLogger<VendorProfileIndexReader>.Instance),
            NullLogger<SystemProfileIngestionService>.Instance);
        await ingestionService.IngestAsync(leaf, cancellationToken);

        var machine = (await machineRepo.ListAsync(cancellationToken)).Single(p => p.Name == machineProfileName);
        var process = (await processRepo.ListAsync(cancellationToken)).Single(p => p.Name == processProfileName);
        var filament = (await filamentRepo.ListAsync(cancellationToken)).Single(p => p.Name == filamentProfileName);

        var printer = Printer.Create(
            Organization.WellKnownId, "Real Slicer Test Printer", "Bambu", "X1C", "10.0.0.99", $"REALSLICER-{Guid.NewGuid():N}");
        printer.LinkMachineProfile(machine.Id);
        printer.LinkProcessProfile(process.Id);
        // 012 — TexturedPei is X1C's real default_bed_type (research.md R2), so every real ingested
        // Bambu filament preset declares a genuine textured_plate_temp value; RealSlicerGoldenFileTests'
        // own WithBedTypeOverride still wins for cases with an explicit manifest.BedType, applied after
        // this resolver call returns.
        printer.SelectBuildPlate(BuildPlateType.TexturedPei);
        await printerRepo.AddAsync(printer, cancellationToken);
        await printerRepo.SaveChangesAsync(cancellationToken);

        var resolver = new ResolveSlicingProfileSelection(
            printerRepo, machineRepo, processRepo, filamentRepo,
            new RepresentativeFilamentResolver(
                filamentRepo, new EmptySpoolRepository(), new EmptyFilamentRepository(), new EmptyFeedUnitRepository()));

        return new StagedProfileSelection(resolver, printer.Id, filament.Id);
    }

    private sealed class MutableMachineProfileRepository : IMachineProfileRepository
    {
        private readonly List<MachineProfile> _rows = [];

        public Task AddAsync(MachineProfile profile, CancellationToken cancellationToken)
        {
            _rows.Add(profile);
            return Task.CompletedTask;
        }

        public Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(_rows);

        public Task<IReadOnlyList<MachineProfile>> FindByNameAsync(ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(_rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task<MachineProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.VendorName == vendorName && p.ExternalId == externalId));

        public Task<MachineProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

        public Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
            string? searchText, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by the real-slicer golden-file harness.");

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableProcessProfileRepository : IProcessProfileRepository
    {
        private readonly List<ProcessProfile> _rows = [];

        public Task AddAsync(ProcessProfile profile, CancellationToken cancellationToken)
        {
            _rows.Add(profile);
            return Task.CompletedTask;
        }

        public Task<ProcessProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(_rows);

        public Task<IReadOnlyList<ProcessProfile>> FindByNameAsync(ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(_rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task<ProcessProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.VendorName == vendorName && p.ExternalId == externalId));

        public Task<ProcessProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

        public Task<(IReadOnlyList<ProcessProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
            string? searchText, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by the real-slicer golden-file harness.");

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableFilamentProfileRepository : IFilamentProfileRepository
    {
        private readonly List<FilamentProfile> _rows = [];

        public Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken)
        {
            _rows.Add(profile);
            return Task.CompletedTask;
        }

        public Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(_rows);

        public Task<IReadOnlyList<FilamentProfile>> FindByNameAsync(ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(_rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task<FilamentProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.VendorName == vendorName && p.ExternalId == externalId));

        public Task<FilamentProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

        public Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
            string? searchText, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by the real-slicer golden-file harness.");

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableVendorRepository : IVendorRepository
    {
        private readonly List<Vendor> _rows = [];

        public Task AddAsync(Vendor vendor, CancellationToken cancellationToken)
        {
            _rows.Add(vendor);
            return Task.CompletedTask;
        }

        public Task<Vendor?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(v => v.Id == id));

        public Task<Vendor?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(v => v.Name == name));

        public Task<IReadOnlyList<Vendor>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Vendor>>(_rows);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutablePrinterRepository : IPrinterRepository
    {
        private readonly List<Printer> _rows = [];

        public Task AddAsync(Printer printer, CancellationToken cancellationToken)
        {
            _rows.Add(printer);
            return Task.CompletedTask;
        }

        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(p => p.SerialNumber == serialNumber));

        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Printer>>(_rows);

        public Task RemoveAsync(Printer printer, CancellationToken cancellationToken)
        {
            _rows.Remove(printer);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Always-empty — the harness always supplies an explicit narrowedFilamentProfileId, so
    // RepresentativeFilamentResolver's spool/library tiers are never actually consulted.
    private sealed class EmptySpoolRepository : ISpoolRepository
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

    private sealed class EmptyFilamentRepository : IFilamentRepository
    {
        public Task AddAsync(Filament filament, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Filament?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Filament?>(null);

        public Task<IReadOnlyList<Filament>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Filament>>([]);

        public Task<FilamentVariant?> FindVariantByIdAsync(Guid filamentVariantId, CancellationToken cancellationToken) =>
            Task.FromResult<FilamentVariant?>(null);

        public Task AddVariantAsync(FilamentVariant variant, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyFeedUnitRepository : IFeedUnitRepository
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
}
