using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Profiles;

namespace OpenFarm.Infrastructure.Tests.Parts.RealSlicer;

internal sealed record StagedProfileSelection(ResolveSlicingProfileSelection Resolver, Guid PrinterId, Guid FilamentProfileId, Guid MachineProfileId, Guid ProcessProfileId, SlicerRef Pin);

/// <summary>Several real catalogs in one place: look rows up by catalog (engine + version) and name, resolve them against any pin.</summary>
internal sealed class StagedCatalogs(
    ResolveSlicingProfileSelection resolver, ProfileResolver profiles, Printer printer,
    IMachineProfileRepository machines, IProcessProfileRepository processes, IFilamentProfileRepository filaments)
{
    public ResolveSlicingProfileSelection Resolver { get; } = resolver;
    public ProfileResolver Profiles { get; } = profiles;
    public Printer Printer { get; } = printer;

    public Task<IReadOnlyList<MachineProfile>> MachinesAsync(SlicerRef catalog) =>
        machines.ListSystemCatalogAsync(FormatOf(catalog), catalog.Version, CancellationToken.None);

    public Task<IReadOnlyList<ProcessProfile>> ProcessesAsync(SlicerRef catalog) =>
        processes.ListSystemCatalogAsync(FormatOf(catalog), catalog.Version, CancellationToken.None);

    public Task<IReadOnlyList<FilamentProfile>> FilamentsAsync(SlicerRef catalog) =>
        filaments.ListSystemCatalogAsync(FormatOf(catalog), catalog.Version, CancellationToken.None);

    public async Task<MachineProfile> MachineAsync(SlicerRef catalog, string name) => (await MachinesAsync(catalog)).Single(p => p.Name == name);

    public async Task<ProcessProfile> ProcessAsync(SlicerRef catalog, string name) => (await ProcessesAsync(catalog)).Single(p => p.Name == name);

    public async Task<FilamentProfile> FilamentAsync(SlicerRef catalog, string name) => (await FilamentsAsync(catalog)).Single(p => p.Name == name);

    private static ProfileSourceFormat FormatOf(SlicerRef catalog) =>
        ProfileEngineCompatibility.NativeFormat(catalog.Engine) ?? throw new ArgumentException($"{catalog.Engine} has no profile catalog.");
}

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
        var pin = new SlicerRef(engine, version);
        var catalogs = await StageCatalogsAsync([pin], cancellationToken);
        var machine = await catalogs.MachineAsync(pin, machineProfileName);
        var process = await catalogs.ProcessAsync(pin, processProfileName);
        var filament = await catalogs.FilamentAsync(pin, filamentProfileName);

        catalogs.Printer.LinkMachineProfile(machine.Id);
        catalogs.Printer.LinkProcessProfile(process.Id);

        return new StagedProfileSelection(catalogs.Resolver, catalogs.Printer.Id, filament.Id, machine.Id, process.Id, pin);
    }

    /// <summary>
    /// Ingests every given slicer's real catalog into one set of repositories — what the database holds once several
    /// bundled slicers were ingested — so a selection made in one engine's catalog can be resolved against another's.
    /// </summary>
    public static async Task<StagedCatalogs> StageCatalogsAsync(IReadOnlyList<SlicerRef> catalogs, CancellationToken cancellationToken)
    {
        var machineRepo = new MutableMachineProfileRepository();
        var processRepo = new MutableProcessProfileRepository();
        var filamentRepo = new MutableFilamentProfileRepository();
        var vendorRepo = new MutableVendorRepository();
        var printerRepo = new MutablePrinterRepository();

        var ingestionService = new SystemProfileIngestionService(
            machineRepo, processRepo, filamentRepo, vendorRepo,
            new VendorProfileIndexReader(NullLogger<VendorProfileIndexReader>.Instance),
            NullLogger<SystemProfileIngestionService>.Instance);
        foreach (var catalog in catalogs)
        {
            await ingestionService.IngestAsync(RealSlicerBundleLocator.GetLeafDirectory(catalog.Engine, catalog.Version), cancellationToken);
        }

        var printer = Printer.Create(
            "Real Slicer Test Printer", "Bambu", "X1C", "10.0.0.99", $"REALSLICER-{Guid.NewGuid():N}");
        // 012 — TexturedPei is X1C's real default_bed_type (research.md R2), so every real ingested
        // Bambu filament preset declares a genuine textured_plate_temp value; RealSlicerGoldenFileTests'
        // own WithBedTypeOverride still wins for cases with an explicit manifest.BedType, applied after
        // this resolver call returns.
        printer.SelectBuildPlate(BuildPlateType.TexturedPei);
        await printerRepo.AddAsync(printer, cancellationToken);
        await printerRepo.SaveChangesAsync(cancellationToken);

        var profileCompatibility = new ProfileCompatibility(machineRepo, filamentRepo);
        var profileResolver = new ProfileResolver(machineRepo, processRepo, filamentRepo);
        var resolver = new ResolveSlicingProfileSelection(
            printerRepo, machineRepo, processRepo, filamentRepo,
            new RepresentativeFilamentResolver(
                filamentRepo, new EmptySpoolRepository(), new EmptyFilamentRepository(), new EmptyFeedUnitRepository(), printerRepo, machineRepo, profileCompatibility),
            profileCompatibility, profileResolver);

        return new StagedCatalogs(resolver, profileResolver, printer, machineRepo, processRepo, filamentRepo);
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

        public Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(
                _rows.Where(p => scope is null || scope.Includes(p.Origin, p.SourceFormat, p.SourceVersion)).ToList());

        public Task<IReadOnlyList<MachineProfile>> FindByNameInCatalogAsync(
            ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name
                    && (p.Origin == ProfileOrigin.User || p.SourceVersion == sourceVersion)).ToList());

        public Task<IReadOnlyList<MachineProfile>> FindByNameAnyVersionAsync(
            ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task<IReadOnlyList<MachineProfile>> FindInCatalogByIdentityAsync(
            ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion
                    && ((externalId != null && p.ExternalId == externalId) || (p.VendorName == vendorName && p.Name == name))).ToList());

        public Task<IReadOnlyList<MachineProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachineProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion).ToList());

        public void Stage(MachineProfile profile) => _rows.Add(profile);

        public Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
                    ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
                    string? searchText, int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null, ProfileCatalogScope? catalog = null) =>
                    throw new NotSupportedException("Not exercised by the real-slicer golden-file harness.");

        public Task<IReadOnlyList<string>> ListIngestedVersionsAsync(ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion is not null)
                    .Select(p => p.SourceVersion!).Distinct().ToList());

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

        public Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(
                _rows.Where(p => scope is null || scope.Includes(p.Origin, p.SourceFormat, p.SourceVersion)).ToList());

        public Task<IReadOnlyList<ProcessProfile>> FindByNameInCatalogAsync(
            ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name
                    && (p.Origin == ProfileOrigin.User || p.SourceVersion == sourceVersion)).ToList());

        public Task<IReadOnlyList<ProcessProfile>> FindByNameAnyVersionAsync(
            ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task<IReadOnlyList<ProcessProfile>> FindInCatalogByIdentityAsync(
            ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion
                    && ((externalId != null && p.ExternalId == externalId) || (p.VendorName == vendorName && p.Name == name))).ToList());

        public Task<IReadOnlyList<ProcessProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion).ToList());

        public void Stage(ProcessProfile profile) => _rows.Add(profile);

        public Task<(IReadOnlyList<ProcessProfile> Items, int TotalCount)> SearchAsync(
                    ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
                    string? searchText, int page, int pageSize, CancellationToken cancellationToken, ProfileCatalogScope? catalog = null) =>
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

        public Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(
                _rows.Where(p => scope is null || scope.Includes(p.Origin, p.SourceFormat, p.SourceVersion)).ToList());

        public Task<IReadOnlyList<FilamentProfile>> FindByNameInCatalogAsync(
            ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name
                    && (p.Origin == ProfileOrigin.User || p.SourceVersion == sourceVersion)).ToList());

        public Task<IReadOnlyList<FilamentProfile>> FindByNameAnyVersionAsync(
            ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

        public Task<IReadOnlyList<FilamentProfile>> FindInCatalogByIdentityAsync(
            ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion
                    && ((externalId != null && p.ExternalId == externalId) || (p.VendorName == vendorName && p.Name == name))).ToList());

        public Task<IReadOnlyList<FilamentProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FilamentProfile>>(
                _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion).ToList());

        public void Stage(FilamentProfile profile) => _rows.Add(profile);

        public Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
                    ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType,
                    string? searchText, int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null, ProfileCatalogScope? catalog = null) =>
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
