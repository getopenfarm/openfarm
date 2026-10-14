using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production.Fakes;

/// <summary>Shared in-memory/recording test doubles for T037's orchestration-only Production command handler tests.</summary>
public sealed class InMemoryPrintJobRepository(params PrintJob[] seedJobs) : IPrintJobRepository
{
    private readonly Dictionary<Guid, PrintJob> _jobs = seedJobs.ToDictionary(j => j.Id);

    public Task AddAsync(PrintJob job, CancellationToken cancellationToken)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            _jobs[job.Id] = job;
        }

        return Task.CompletedTask;
    }

    public Task<PrintJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_jobs.GetValueOrDefault(id));

    public Task<IReadOnlyList<PrintJob>> ListByPartRequirementIdAsync(Guid partRequirementId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintJob>>(_jobs.Values.Where(j => j.PartRequirementId == partRequirementId).ToList());

    public Task<IReadOnlyList<PrintJob>> ListByProductionRunIdAsync(Guid productionRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintJob>>(JobsByRunId.GetValueOrDefault(productionRunId, []));

    /// <summary>Test seam: since PrintJob carries no direct RunId, callers assign this map themselves per test.</summary>
    public Dictionary<Guid, List<PrintJob>> JobsByRunId { get; } = [];

    public Task<PrintJob?> GetActiveJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        Task.FromResult(_jobs.Values.FirstOrDefault(j => j.DispatchedPrinterId == printerId));

    public Task<IReadOnlyList<PrintJob>> ListDispatchInFlightAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintJob>>([]);

    public Task<double?> GetMaxQueuePositionAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(_jobs.Values.Count == 0 ? (double?)null : _jobs.Values.Max(j => j.QueuePosition));

    public Task<(IReadOnlyList<PrintJob> Jobs, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts)> ListQueueAsync(
        bool backfillOnly, int page, int pageSize, CancellationToken cancellationToken, string? status = null, string? search = null) =>
        Task.FromResult<(IReadOnlyList<PrintJob>, int, IReadOnlyDictionary<string, int>)>(([], 0, new Dictionary<string, int>()));

    public Task<PrintJob?> GetNextQueuedJobAsync(double afterQueuePosition, Guid excludeJobId, CancellationToken cancellationToken) =>
        Task.FromResult(_jobs.Values
            .Where(j => j.Status == PrintJobStatus.Queued && j.Id != excludeJobId && j.QueuePosition > afterQueuePosition)
            .OrderBy(j => j.QueuePosition)
            .FirstOrDefault());

    public Task<double?> GetMinQueuedPositionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_jobs.Values.Where(j => j.Status == PrintJobStatus.Queued).Select(j => (double?)j.QueuePosition).Min());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class InMemoryProductionRunRepository(params ProductionRun[] runs) : IProductionRunRepository
{
    private readonly Dictionary<Guid, ProductionRun> _runs = runs.ToDictionary(r => r.Id);

    public Task AddAsync(ProductionRun run, CancellationToken cancellationToken)
    {
        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task<ProductionRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_runs.GetValueOrDefault(id));

    public Task<(IReadOnlyList<ProductionRun> Runs, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null) =>
        Task.FromResult<(IReadOnlyList<ProductionRun>, int)>((_runs.Values.ToList(), _runs.Count));

    public Task<ProductionRun?> GetByRunLineIdAsync(Guid runLineId, CancellationToken cancellationToken) =>
        Task.FromResult(_runs.Values.FirstOrDefault(r => r.Lines.Any(l => l.Id == runLineId)));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class InMemoryPartRequirementRepository(params PartRequirement[] seedRequirements) : IPartRequirementRepository
{
    private readonly Dictionary<Guid, PartRequirement> _requirements = seedRequirements.ToDictionary(r => r.Id);

    public Task<bool> HasPartReferenceAsync(Guid partId, CancellationToken cancellationToken) =>
        Task.FromResult(_requirements.Values.Any(r => r.PartId == partId));

    public Task AddRangeAsync(IReadOnlyList<PartRequirement> requirements, CancellationToken cancellationToken)
    {
        foreach (var requirement in requirements)
        {
            _requirements[requirement.Id] = requirement;
        }

        return Task.CompletedTask;
    }

    public Task<PartRequirement?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_requirements.GetValueOrDefault(id));

    public Task<IReadOnlyList<PartRequirement>> ListByRunLineIdAsync(Guid runLineId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PartRequirement>>(_requirements.Values.Where(r => r.RunLineId == runLineId).ToList());

    public Task<IReadOnlyList<PartRequirement>> ListByProductionRunIdAsync(Guid productionRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PartRequirement>>([]);
}

public sealed class InMemoryPartRepository(params Part[] parts) : IPartRepository
{
    private readonly Dictionary<Guid, Part> _parts = parts.ToDictionary(p => p.Id);

    public Task AddAsync(Part part, CancellationToken cancellationToken)
    {
        _parts[part.Id] = part;
        return Task.CompletedTask;
    }

    public Task<Part?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_parts.GetValueOrDefault(id));

    public Task<(IReadOnlyList<Part> Parts, int TotalCount)> ListAsync(
        IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null) =>
        Task.FromResult<(IReadOnlyList<Part>, int)>((_parts.Values.ToList(), _parts.Count));

    public Task<IReadOnlyList<Part>> ListWithResolvingEstimateAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Part>>([]);

    public Task RemoveAsync(Part part, CancellationToken cancellationToken)
    {
        _parts.Remove(part.Id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class InMemoryPrinterRepository(params Printer[] printers) : IPrinterRepository
{
    private readonly Dictionary<Guid, Printer> _printers = printers.ToDictionary(p => p.Id);

    public Task AddAsync(Printer printer, CancellationToken cancellationToken)
    {
        _printers[printer.Id] = printer;
        return Task.CompletedTask;
    }

    public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_printers.GetValueOrDefault(id));

    public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
        Task.FromResult(_printers.Values.FirstOrDefault(p => p.SerialNumber == serialNumber));

    public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Printer>>(_printers.Values.ToList());

    public Task RemoveAsync(Printer printer, CancellationToken cancellationToken)
    {
        _printers.Remove(printer.Id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class RecordingProductionEventPublisher : IProductionEventPublisher
{
    public List<(Guid RunId, ProductionRunStatus Status)> RunStatusChanges { get; } = [];

    public List<(Guid JobId, PrintJobStatus Status)> JobStatusChanges { get; } = [];

    public List<(Guid JobId, Guid PrinterId)> Dispatches { get; } = [];

    public Task ProductionRunStatusChangedAsync(Guid organizationId, Guid productionRunId, ProductionRunStatus status, CancellationToken cancellationToken)
    {
        RunStatusChanges.Add((productionRunId, status));
        return Task.CompletedTask;
    }

    public Task PrintJobStatusChangedAsync(Guid organizationId, Guid printJobId, PrintJobStatus status, CancellationToken cancellationToken)
    {
        JobStatusChanges.Add((printJobId, status));
        return Task.CompletedTask;
    }

    public Task PrintJobDispatchedAsync(Guid organizationId, Guid printJobId, Guid printerId, CancellationToken cancellationToken)
    {
        Dispatches.Add((printJobId, printerId));
        return Task.CompletedTask;
    }
}

public sealed class InMemorySpoolRepository(params Spool[] spools) : ISpoolRepository
{
    private readonly Dictionary<Guid, Spool> _spools = spools.ToDictionary(s => s.Id);

    public Task AddAsync(Spool spool, CancellationToken cancellationToken)
    {
        _spools[spool.Id] = spool;
        return Task.CompletedTask;
    }

    public Task<Spool?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_spools.GetValueOrDefault(id));

    public Task<IReadOnlyList<Spool>> ListAsync(SpoolState? state, Guid? filamentVariantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Spool>>(_spools.Values.ToList());

    public Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Spool>>([]);

    public Task<Spool?> FindUsingFeedSlotAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
        Task.FromResult<Spool?>(null);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class FakeAssemblyGraphQueryService(BomExplosionResult result) : IAssemblyGraphQueryService
{
    public Task<BomExplosionResult> ExplodeAsync(Guid organizationId, Guid rootAssemblyId, int quantity, CancellationToken cancellationToken) =>
        Task.FromResult(result);

    public Task<IReadOnlyList<(Guid ParentAssemblyId, Guid ChildAssemblyId)>> GetOrganizationEdgesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(Guid, Guid)>>([]);
}

public sealed class InMemoryConnectionSecretRepository(params ConnectionSecret[] secrets) : IConnectionSecretRepository
{
    private readonly List<ConnectionSecret> _secrets = secrets.ToList();

    public Task AddAsync(ConnectionSecret connectionSecret, CancellationToken cancellationToken)
    {
        _secrets.Add(connectionSecret);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConnectionSecret>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConnectionSecret>>(_secrets);

    public Task<ConnectionSecret?> GetByPrinterSerialNumberAsync(string printerSerialNumber, CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.FirstOrDefault(s => s.PrinterSerialNumber == printerSerialNumber));
}

public sealed class PassthroughSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;

    public string Unprotect(string ciphertext) => ciphertext;
}

public sealed class RecordingPrinterConnector : IPrinterConnector
{
    public List<string> StartedJobNames { get; } = [];

    public int PauseCalls { get; private set; }

    public int ResumeCalls { get; private set; }

    public int StopCalls { get; private set; }

    public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.FromResult(true);

    public IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        AsyncEmpty();

    private static async IAsyncEnumerable<PrinterConnectorEvent> AsyncEmpty()
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, CancellationToken cancellationToken)
    {
        StartedJobNames.Add(jobName);
        return Task.CompletedTask;
    }

    public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        PauseCalls++;
        return Task.CompletedTask;
    }

    public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        ResumeCalls++;
        return Task.CompletedTask;
    }

    public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        StopCalls++;
        return Task.CompletedTask;
    }
}

public sealed class RecordingPrinterConnectorFactory(RecordingPrinterConnector connector) : IPrinterConnectorFactory
{
    public IPrinterConnector CreateConnector(PrinterConnectionDetails details) => connector;

    public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) =>
        throw new NotSupportedException("Not needed by these orchestration tests.");
}

public static class ProductionTestFixtures
{
    /// <summary>
    /// Every registered (Brand, ModelCode) entry in PrinterModelCatalog is Bambu or System, both
    /// live-integrated (see its own remarks: "there is no non-live brand registrable today" until
    /// Phase 15) — there is no manual-brand equivalent constructible via Printer.Create yet.
    /// </summary>
    public static Printer CreateLiveIntegratedPrinter(string? serialNumber = null) =>
        Printer.Create(Guid.NewGuid(), "Bambu printer", "Bambu", "X1C", "10.0.0.5", serialNumber ?? $"SN-{Guid.NewGuid():N}");

    public static Part CreatePart(int unitsPerPrint = 1, string? revisionHash = null, int revisionNumber = 1) =>
        Part.Create(
            Guid.NewGuid(), "Test part", PartFileFormat.ThreeMf, requiresSlicing: false, "widget.3mf", "path",
            fileSizeBytes: 10, nozzleDiameterMm: 0.4m, slicerEngineOverride: null, slicerVersionUsed: null,
            EstimateStatus.Available, estimatedPrintTimeMinutes: 30, estimatedFilamentWeightGrams: 10m,
            plateCount: 1, unitsPerPrint: unitsPerPrint, revisionHash: revisionHash ?? $"{Guid.NewGuid():N}",
            [new FilamentRequirementSpec("PLA", null, null, null, null)], thumbnails: [], tags: []);

    public static PrintJob CreateQueuedJob(Guid partRequirementId, double queuePosition = 1024) =>
        PrintJob.Create(
            Guid.NewGuid(), partRequirementId, queuePosition, plateQuantity: 1,
            AssignmentMode.Unassigned, pinnedPrinterId: null, fileRevisionHash: $"{Guid.NewGuid():N}", fileRevisionNumber: 1,
            filamentRequirements: [new JobFilamentRequirementSpec("PLA", null)]);
}

public sealed class NoopContinuationResolver : IAutoContinuationResolver
{
    public Task<AutoContinuationResult> ResolveAsync(Spool depletedSpool, CancellationToken cancellationToken) =>
        Task.FromResult(AutoContinuationResult.None);
}
