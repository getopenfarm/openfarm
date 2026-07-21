using Finbuckle.MultiTenant.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

/// <summary>
/// Guards the two overlapping strand-the-row failure modes this feature has been bitten by, both of
/// which left a persisted job/part in a non-terminal status forever (the frontend has no polling
/// fallback, so the UI just spun):
/// <list type="number">
/// <item>An unexpected exception at either processing milestone (e.g. a raw <c>IOException</c> from
/// <c>IPartFileStorage.OpenReadAsync</c>, which <c>LocalDiskPartFileStorage</c> never wraps) escaping
/// a too-narrow catch — now a genuine catch-all (excluding cancellation) marks the job Failed.</item>
/// <item>A row orphaned by the host restarting mid-job — the in-memory work queue is never durable
/// across restarts, so the startup reconciliation pass drives anything left in-flight to a terminal
/// state (Failed/Unavailable) before workers begin, since nothing else ever picks it back up.</item>
/// </list>
/// See <see cref="PartProcessingBackgroundService"/>. Test-after (research.md §12), mirroring
/// <see cref="Printers.PrinterConnectionBackgroundServiceTests"/>'s harness.
/// </summary>
public class PartProcessingBackgroundServiceTests
{
    private static readonly Guid OrgId = Organization.WellKnownId;

    // -- Widened exception handling on the processing path --------------------------------------

    [Fact]
    public async Task StorageReadThrowsDuringMilestone1_MarksJobFailed_WithoutEverReachingReadyForReview()
    {
        var organizationId = Guid.NewGuid();
        var job = PartProcessingJob.CreateAdd(
            organizationId, PartFileFormat.Stl, "model.stl", "orgs/x/job.stl", 1024,
            requiresSlicing: true, SlicerEngine.OrcaSlicer, "2.2.0", 0.4m, "Generic PLA", requestedByUserId: null);
        var storage = new SequencedPartFileStorage(() => throw new UnauthorizedAccessException("permission denied"));
        var processor = new FixedPartFileProcessor();
        var publisher = new RecordingPartEventPublisher();

        var provider = BuildServiceProvider(job, storage, processor, publisher, out _);
        var service = new PartProcessingBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PartProcessingBackgroundService>.Instance, maxConcurrency: 1);

        await service.StartAsync(CancellationToken.None);
        try
        {
            service.Enqueue(new PartProcessingWorkItem(organizationId, job.Id));

            await WaitUntilAsync(() => job.Status == PartProcessingJobStatus.Failed, TimeSpan.FromSeconds(2));

            job.ErrorMessage.Should().Be("permission denied");
            publisher.StatusSnapshots.Should().Equal(PartProcessingJobStatus.Failed);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StorageReadThrowsDuringMilestone2_MarksJobFailed_AfterAlreadyReachingReadyForReview()
    {
        var organizationId = Guid.NewGuid();
        var job = PartProcessingJob.CreateAdd(
            organizationId, PartFileFormat.Stl, "model.stl", "orgs/x/job.stl", 1024,
            requiresSlicing: true, SlicerEngine.OrcaSlicer, "2.2.0", 0.4m, "Generic PLA", requestedByUserId: null);
        // First OpenReadAsync call (milestone 1) succeeds; second (milestone 2's estimate
        // continuation, since ImmediateEstimate is null below) throws a raw, unwrapped IOException —
        // exactly what LocalDiskPartFileStorage.OpenReadAsync's bare File.OpenRead can produce for a
        // misconfigured PartFileStorage:RootDirectory.
        var storage = new SequencedPartFileStorage(
            () => new MemoryStream([1, 2, 3]),
            () => throw new IOException("disk read failed"));
        var processor = new FixedPartFileProcessor
        {
            MaterialResolution = new PartMaterialResolution(0.4m, [], PlateCount: 1, ImmediateEstimate: null),
        };
        var publisher = new RecordingPartEventPublisher();

        var provider = BuildServiceProvider(job, storage, processor, publisher, out var activityLogWriter);
        var service = new PartProcessingBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PartProcessingBackgroundService>.Instance, maxConcurrency: 1);

        await service.StartAsync(CancellationToken.None);
        try
        {
            service.Enqueue(new PartProcessingWorkItem(organizationId, job.Id));

            await WaitUntilAsync(() => job.Status == PartProcessingJobStatus.Failed, TimeSpan.FromSeconds(2));

            job.ErrorMessage.Should().Be("disk read failed");
            publisher.StatusSnapshots.Should().Equal(PartProcessingJobStatus.ReadyForReview, PartProcessingJobStatus.Failed);
            activityLogWriter.Entries.Should().ContainSingle(e =>
                e.EntityId == job.Id.ToString() && e.EventType == "PartFileProcessingFailed");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // -- Startup reconciliation of work orphaned by a mid-processing restart --------------------

    [Fact]
    public async Task Reconciliation_OrphanedProcessingJob_IsMarkedFailedOnStartup()
    {
        var job = ProcessingJob();
        var jobs = new ReconcilingJobRepository(job);
        var publisher = new RecordingPartEventPublisher();
        var activityLog = new RecordingActivityLogWriter();

        var service = BuildService(jobs, new SeededPartRepository(), new StubPartFileStorage(), new FixedPartFileProcessor(), publisher, activityLog);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => job.Status == PartProcessingJobStatus.Failed, TimeSpan.FromSeconds(2));

            job.Status.Should().Be(PartProcessingJobStatus.Failed);
            job.ErrorMessage.Should().Contain("interrupted");
            activityLog.Entries.Should().ContainSingle(e =>
                e.EntityType == "PartProcessingJob" && e.EntityId == job.Id.ToString() && e.EventType == "PartFileProcessingFailed");
            publisher.JobStatusChanges.Should().Contain(job.Id);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Reconciliation_OrphanedReadyForReviewWithResolvingPreview_IsMarkedFailedOnStartup()
    {
        var job = ProcessingJob();
        job.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PLA", null, null)], plateCount: 1,
            EstimateStatus.Resolving, estimatedPrintTimeMinutes: null, estimatedFilamentWeightGrams: null, thumbnails: []);

        var jobs = new ReconcilingJobRepository(job);
        var service = BuildService(jobs, new SeededPartRepository(), new StubPartFileStorage(), new FixedPartFileProcessor(),
            new RecordingPartEventPublisher(), new RecordingActivityLogWriter());

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => job.Status == PartProcessingJobStatus.Failed, TimeSpan.FromSeconds(2));
            job.Status.Should().Be(PartProcessingJobStatus.Failed);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Reconciliation_ConfirmedPartWithResolvingEstimate_IsMarkedUnavailableOnStartup()
    {
        var part = ResolvingPart();
        var parts = new SeededPartRepository(part);
        var publisher = new RecordingPartEventPublisher();
        var activityLog = new RecordingActivityLogWriter();

        var service = BuildService(new ReconcilingJobRepository(), parts, new StubPartFileStorage(),
            new FixedPartFileProcessor(), publisher, activityLog);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => part.EstimateStatus == EstimateStatus.Unavailable, TimeSpan.FromSeconds(2));

            part.EstimateStatus.Should().Be(EstimateStatus.Unavailable);
            publisher.EstimateUnavailable.Should().Contain(part.Id);
            activityLog.Entries.Should().ContainSingle(e =>
                e.EntityType == "Part" && e.EntityId == part.Id.ToString() && e.EventType == "PartEstimateUnavailable");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Reconciliation_LeavesTerminalAndAlreadyCompleteRowsUntouched()
    {
        // A job that already resolved its preview, a confirmed job (terminal), and a part whose
        // estimate is already Available. Over-eager reconciliation would wrongly fail/unavailable
        // these; it must not.
        var readyAvailable = ProcessingJob();
        readyAvailable.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PLA", null, null)], plateCount: 1,
            EstimateStatus.Available, estimatedPrintTimeMinutes: 42, estimatedFilamentWeightGrams: 12.5m, thumbnails: []);

        var confirmed = ProcessingJob();
        confirmed.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PLA", null, null)], plateCount: 1,
            EstimateStatus.Available, estimatedPrintTimeMinutes: 42, estimatedFilamentWeightGrams: 12.5m, thumbnails: []);
        confirmed.Confirm(Guid.NewGuid());

        var availablePart = AvailablePart();

        var jobs = new ReconcilingJobRepository(readyAvailable, confirmed);
        var parts = new SeededPartRepository(availablePart);
        var publisher = new RecordingPartEventPublisher();
        var activityLog = new RecordingActivityLogWriter();

        var service = BuildService(jobs, parts, new StubPartFileStorage(), new FixedPartFileProcessor(), publisher, activityLog);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // Reconciliation runs first thing in ExecuteAsync; give it ample time to (wrongly) act.
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            readyAvailable.Status.Should().Be(PartProcessingJobStatus.ReadyForReview);
            readyAvailable.PreviewEstimateStatus.Should().Be(EstimateStatus.Available);
            confirmed.Status.Should().Be(PartProcessingJobStatus.Confirmed);
            availablePart.EstimateStatus.Should().Be(EstimateStatus.Available);

            publisher.JobStatusChanges.Should().BeEmpty();
            publisher.EstimateUnavailable.Should().BeEmpty();
            activityLog.Entries.Should().BeEmpty();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // -- Consume loop (runs only after the one-time reconciliation completes) -------------------

    [Fact]
    public async Task ConsumeLoop_EnqueuedWorkItem_ProcessesJobToReadyForReview()
    {
        var jobs = new ReconcilingJobRepository();
        var publisher = new RecordingPartEventPublisher();
        var processor = new ConfigurablePartFileProcessor
        {
            Material = _ => new PartMaterialResolution(
                0.4m,
                [new PreviewFilamentRequirement(0, "PLA", null, null)],
                PlateCount: 1,
                ImmediateEstimate: new PartEstimateResolution(42, 12.5m, [])),
        };

        var service = BuildService(jobs, new SeededPartRepository(), new StubPartFileStorage(), processor, publisher, new RecordingActivityLogWriter());

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForReconciliationToSettleAsync();

            // Only now — after startup reconciliation has drained — is a fresh job enqueued, exactly
            // as AddPartCommandHandler does inside the already-running process.
            var job = ProcessingJob();
            jobs.Add(job);
            service.Enqueue(new PartProcessingWorkItem(OrgId, job.Id));

            await WaitUntilAsync(() => job.Status == PartProcessingJobStatus.ReadyForReview, TimeSpan.FromSeconds(2));

            job.Status.Should().Be(PartProcessingJobStatus.ReadyForReview);
            job.PreviewEstimateStatus.Should().Be(EstimateStatus.Available);
            job.PreviewEstimatedPrintTimeMinutes.Should().Be(42);
            publisher.JobStatusChanges.Should().Contain(job.Id);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ConsumeLoop_UnexpectedExceptionOnOneItem_MarksItFailedWithoutStoppingTheNext()
    {
        // A non-domain exception is caught by the widened catch-all and marks that job Failed; the
        // worker (and host) must survive so the next item still processes to completion.
        var poison = ProcessingJob();
        var healthy = ProcessingJob();

        var jobs = new ReconcilingJobRepository();
        var processor = new ConfigurablePartFileProcessor
        {
            Material = job => job.Id == poison.Id
                ? throw new InvalidOperationException("boom")
                : new PartMaterialResolution(0.4m, [new PreviewFilamentRequirement(0, "PLA", null, null)], 1,
                    new PartEstimateResolution(42, 12.5m, [])),
        };

        var service = BuildService(jobs, new SeededPartRepository(), new StubPartFileStorage(), processor,
            new RecordingPartEventPublisher(), new RecordingActivityLogWriter(), maxConcurrency: 1);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForReconciliationToSettleAsync();

            jobs.Add(poison);
            jobs.Add(healthy);
            service.Enqueue(new PartProcessingWorkItem(OrgId, poison.Id));
            service.Enqueue(new PartProcessingWorkItem(OrgId, healthy.Id));

            await WaitUntilAsync(() => healthy.Status == PartProcessingJobStatus.ReadyForReview, TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => poison.Status == PartProcessingJobStatus.Failed, TimeSpan.FromSeconds(2));

            poison.Status.Should().Be(PartProcessingJobStatus.Failed);
            poison.ErrorMessage.Should().Be("boom");
            healthy.Status.Should().Be(PartProcessingJobStatus.ReadyForReview);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ConsumeLoop_DomainParsingFailure_MarksThatJobFailed()
    {
        var jobs = new ReconcilingJobRepository();
        var activityLog = new RecordingActivityLogWriter();
        var processor = new ConfigurablePartFileProcessor
        {
            Material = _ => throw new PartFileParsingFailedException("corrupt file"),
        };

        var service = BuildService(jobs, new SeededPartRepository(), new StubPartFileStorage(), processor,
            new RecordingPartEventPublisher(), activityLog);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForReconciliationToSettleAsync();

            var job = ProcessingJob();
            jobs.Add(job);
            service.Enqueue(new PartProcessingWorkItem(OrgId, job.Id));

            await WaitUntilAsync(() => job.Status == PartProcessingJobStatus.Failed, TimeSpan.FromSeconds(2));

            job.Status.Should().Be(PartProcessingJobStatus.Failed);
            job.ErrorMessage.Should().Contain("corrupt file");
            activityLog.Entries.Should().Contain(e => e.EventType == "PartFileProcessingFailed");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // -- Fixtures ------------------------------------------------------------------------------

    private static PartProcessingJob ProcessingJob() =>
        PartProcessingJob.CreateAdd(OrgId, PartFileFormat.ThreeMf, "widget.3mf", storagePath: "path", fileSizeBytes: 10,
            requiresSlicing: true, SlicerEngine.OrcaSlicer, slicerVersionUsed: "2.2.0", targetNozzleDiameterMm: 0.4m, targetMaterialType: null, requestedByUserId: null);

    private static Part ResolvingPart() =>
        Part.Create(OrgId, "Resolving part", PartFileFormat.ThreeMf, requiresSlicing: true, "widget.3mf", "path",
            fileSizeBytes: 10, nozzleDiameterMm: 0.4m, slicerEngineOverride: null, slicerVersionUsed: null, EstimateStatus.Resolving,
            estimatedPrintTimeMinutes: null, estimatedFilamentWeightGrams: null, plateCount: 1,
            [new FilamentRequirementSpec("PLA", null, null, null, null)], thumbnails: [], tags: []);

    private static Part AvailablePart() =>
        Part.Create(OrgId, "Available part", PartFileFormat.ThreeMf, requiresSlicing: true, "widget.3mf", "path",
            fileSizeBytes: 10, nozzleDiameterMm: 0.4m, slicerEngineOverride: null, slicerVersionUsed: null, EstimateStatus.Available,
            estimatedPrintTimeMinutes: 42, estimatedFilamentWeightGrams: 12.5m, plateCount: 1,
            [new FilamentRequirementSpec("PLA", null, null, null, null)], thumbnails: [], tags: []);

    /// <summary>
    /// Lets the one-time startup reconciliation pass settle before a consume test enqueues fresh work.
    /// These consume tests seed no orphaned rows, so reconciliation enumerates an empty set and is a
    /// no-op — a short, bounded settle wait is enough to guarantee it has run (and the workers have
    /// started) before a genuinely-new job is added and enqueued, matching how AddPartCommandHandler
    /// enqueues into the already-running process.
    /// </summary>
    private static Task WaitForReconciliationToSettleAsync() => Task.Delay(TimeSpan.FromMilliseconds(100));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            if (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Condition not met within {timeout}.");
            }

            await Task.Delay(10, CancellationToken.None);
        }
    }

    // -- Harness builders ----------------------------------------------------------------------

    /// <summary>Single-job harness used by the milestone exception-handling tests (no orphans to reconcile).</summary>
    private static ServiceProvider BuildServiceProvider(
        PartProcessingJob job,
        IPartFileStorage storage,
        IPartFileProcessor processor,
        IPartEventPublisher publisher,
        out RecordingActivityLogWriter activityLogWriter)
    {
        var writer = new RecordingActivityLogWriter();
        activityLogWriter = writer;

        var services = new ServiceCollection();
        services.AddSingleton<IPartProcessingJobRepository>(new SingleJobRepository(job));
        services.AddSingleton<IPartRepository>(new SeededPartRepository());
        services.AddSingleton(storage);
        services.AddSingleton(processor);
        services.AddSingleton(publisher);
        services.AddSingleton<IActivityLogWriter>(writer);

        AddTenantContext(services, job.OrganizationId);
        return services.BuildServiceProvider();
    }

    /// <summary>Multi-row harness used by the reconciliation/consume tests; returns the wired-up service.</summary>
    private static PartProcessingBackgroundService BuildService(
        ReconcilingJobRepository jobs,
        SeededPartRepository parts,
        IPartFileStorage storage,
        IPartFileProcessor processor,
        IPartEventPublisher publisher,
        IActivityLogWriter activityLog,
        int maxConcurrency = 2)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPartProcessingJobRepository>(jobs);
        services.AddSingleton<IPartRepository>(parts);
        services.AddSingleton(storage);
        services.AddSingleton(processor);
        services.AddSingleton(publisher);
        services.AddSingleton(activityLog);

        AddTenantContext(services, OrgId);

        var provider = services.BuildServiceProvider();
        return new PartProcessingBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PartProcessingBackgroundService>.Instance,
            maxConcurrency);
    }

    // CreateTenantScope resolves these per scope (Finbuckle's HTTP-middleware resolution never runs
    // for a scope the background service creates itself) — mirrors PrinterConnectionBackgroundServiceTests.
    private static void AddTenantContext(ServiceCollection services, Guid organizationId)
    {
        var tenantInfo = new AppTenantInfo { Id = organizationId.ToString(), Identifier = OrganizationTenantStore.StaticIdentifier };
        services.AddSingleton<IMultiTenantStore<AppTenantInfo>>(new StubTenantStore(tenantInfo));
        services.AddSingleton<AsyncLocalMultiTenantContextAccessor<AppTenantInfo>>();
        services.AddSingleton<IMultiTenantContextSetter>(sp => sp.GetRequiredService<AsyncLocalMultiTenantContextAccessor<AppTenantInfo>>());
    }

    // -- Test doubles --------------------------------------------------------------------------

    /// <summary>Minimal single-seed job repo that reports no orphans — for tests that only exercise the live processing path.</summary>
    private sealed class SingleJobRepository(PartProcessingJob seedJob) : IPartProcessingJobRepository
    {
        public Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == seedJob.Id ? seedJob : null);

        public Task<IReadOnlyList<PartProcessingJob>> ListOrphanedInFlightAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PartProcessingJob>>([]);

        // The job is mutated in place (shared reference), so there's nothing to flush.
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Job repo with a real orphan-detection query (mirrors PartProcessingJobRepository's WHERE) and mutable seeding.</summary>
    private sealed class ReconcilingJobRepository(params PartProcessingJob[] seed) : IPartProcessingJobRepository
    {
        private readonly Dictionary<Guid, PartProcessingJob> _jobs = seed.ToDictionary(j => j.Id);

        public void Add(PartProcessingJob job) => _jobs[job.Id] = job;

        public Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken)
        {
            _jobs[job.Id] = job;
            return Task.CompletedTask;
        }

        public Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_jobs.GetValueOrDefault(id));

        public Task<IReadOnlyList<PartProcessingJob>> ListOrphanedInFlightAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PartProcessingJob>>(_jobs.Values
                .Where(j => j.Status == PartProcessingJobStatus.Processing
                    || (j.Status == PartProcessingJobStatus.ReadyForReview && j.PreviewEstimateStatus == EstimateStatus.Resolving))
                .ToList());

        // Entities are mutated in place (shared reference), so there's nothing to flush.
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SeededPartRepository(params Part[] seed) : IPartRepository
    {
        private readonly Dictionary<Guid, Part> _parts = seed.ToDictionary(p => p.Id);

        public Task AddAsync(Part part, CancellationToken cancellationToken)
        {
            _parts[part.Id] = part;
            return Task.CompletedTask;
        }

        public Task<Part?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_parts.GetValueOrDefault(id));

        public Task<(IReadOnlyList<Part> Parts, int TotalCount)> ListAsync(
            IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyList<Part>, int)>((_parts.Values.ToList(), _parts.Count));

        public Task<IReadOnlyList<Part>> ListWithResolvingEstimateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Part>>(_parts.Values.Where(p => p.EstimateStatus == EstimateStatus.Resolving).ToList());

        public Task RemoveAsync(Part part, CancellationToken cancellationToken)
        {
            _parts.Remove(part.Id);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Hands back each configured stream/exception in order, matching real storage's shape of throwing synchronously rather than via a faulted Task.</summary>
    private sealed class SequencedPartFileStorage(params Func<Stream>[] reads) : IPartFileStorage
    {
        private int _callCount = -1;

        public Task<string> SaveAsync(Guid organizationId, Guid ownerId, string originalFileName, Stream content, CancellationToken cancellationToken) =>
            Task.FromResult("irrelevant");

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
        {
            var index = Math.Min(Interlocked.Increment(ref _callCount), reads.Length - 1);
            return Task.FromResult(reads[index]());
        }

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubPartFileStorage : IPartFileStorage
    {
        public Task<string> SaveAsync(Guid organizationId, Guid ownerId, string originalFileName, Stream content, CancellationToken cancellationToken) =>
            Task.FromResult("path");

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Returns a fixed milestone-1 resolution; its milestone-2 path is never expected to run.</summary>
    private sealed class FixedPartFileProcessor : IPartFileProcessor
    {
        public PartMaterialResolution MaterialResolution { get; set; } = new(0.4m, [], PlateCount: 1, ImmediateEstimate: null);

        public Task<PartMaterialResolution> ResolveMaterialAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken) =>
            Task.FromResult(MaterialResolution);

        public Task<PartEstimateResolution> ResolveEstimateAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("ResolveEstimateAsync should not be reached in this test.");
    }

    /// <summary>Per-job configurable processor for the consume-loop tests.</summary>
    private sealed class ConfigurablePartFileProcessor : IPartFileProcessor
    {
        public Func<PartProcessingJob, PartMaterialResolution>? Material { get; init; }

        public Func<PartProcessingJob, PartEstimateResolution>? Estimate { get; init; }

        public Task<PartMaterialResolution> ResolveMaterialAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken) =>
            Task.FromResult((Material ?? throw new InvalidOperationException("Material not configured"))(job));

        public Task<PartEstimateResolution> ResolveEstimateAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken) =>
            Task.FromResult((Estimate ?? (_ => new PartEstimateResolution(1, 1m, [])))(job));
    }

    private sealed class RecordingPartEventPublisher : IPartEventPublisher
    {
        public List<PartProcessingJob> StatusChanges { get; } = [];

        // job is a mutable reference type shared across every publish call, so re-reading .Status
        // off a StatusChanges entry after the fact only ever reflects the *current* status, not what
        // it was at publish time — snapshot the enum value (itself immutable) at each call instead.
        public List<PartProcessingJobStatus> StatusSnapshots { get; } = [];

        public List<Guid> JobStatusChanges { get; } = [];

        public List<Guid> EstimateUnavailable { get; } = [];

        public Task PartProcessingJobStatusChangedAsync(Guid organizationId, PartProcessingJob job, CancellationToken cancellationToken)
        {
            StatusChanges.Add(job);
            StatusSnapshots.Add(job.Status);
            JobStatusChanges.Add(job.Id);
            return Task.CompletedTask;
        }

        public Task PartUpsertedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PartEstimateResolvedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PartEstimateUnavailableAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken)
        {
            EstimateUnavailable.Add(partId);
            return Task.CompletedTask;
        }

        public Task PartDeletedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingActivityLogWriter : IActivityLogWriter
    {
        public List<ActivityLogEntry> Entries { get; } = [];

        public Task WriteAsync(ActivityLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTenantStore(AppTenantInfo tenantInfo) : IMultiTenantStore<AppTenantInfo>
    {
        public Task<AppTenantInfo?> GetAsync(string id) => Task.FromResult<AppTenantInfo?>(tenantInfo);

        public Task<AppTenantInfo?> GetByIdentifierAsync(string identifier) => Task.FromResult<AppTenantInfo?>(tenantInfo);

        public Task<IEnumerable<AppTenantInfo>> GetAllAsync() => Task.FromResult<IEnumerable<AppTenantInfo>>([tenantInfo]);

        public Task<IEnumerable<AppTenantInfo>> GetAllAsync(int take, int skip) => Task.FromResult<IEnumerable<AppTenantInfo>>([tenantInfo]);

        public Task<bool> AddAsync(AppTenantInfo tenantInfoToAdd) => Task.FromResult(true);

        public Task<bool> UpdateAsync(AppTenantInfo tenantInfoToUpdate) => Task.FromResult(true);

        public Task<bool> RemoveAsync(string identifier) => Task.FromResult(true);
    }
}
