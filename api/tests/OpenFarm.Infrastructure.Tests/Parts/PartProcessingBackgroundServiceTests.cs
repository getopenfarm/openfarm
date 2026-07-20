using Finbuckle.MultiTenant.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Regression coverage one layer up from CliSlicerEngineTests: that class proves a missing CLI
// binary converts to SlicingFailedException, but PartProcessingBackgroundService's own catches
// used to only recognize specific exception types (PartFileParsingFailedException/
// UnslicedProjectMaterialParsingFailedException for milestone 1, SlicingFailedException for
// milestone 2). Anything else — e.g. a raw IOException from IPartFileStorage.OpenReadAsync, which
// LocalDiskPartFileStorage never wraps — fell through to ConsumeAsync's log-only catch, leaving the
// job stuck non-terminal forever since the frontend has no polling fallback. These tests assert the
// widened catch-all now converts *any* non-cancellation exception in that path into a Failed job.
public class PartProcessingBackgroundServiceTests
{
    [Fact]
    public async Task StorageReadThrowsDuringMilestone1_MarksJobFailed_WithoutEverReachingReadyForReview()
    {
        var organizationId = Guid.NewGuid();
        var job = PartProcessingJob.CreateAdd(
            organizationId, PartFileFormat.Stl, "model.stl", "orgs/x/job.stl", 1024,
            requiresSlicing: true, SlicerEngine.OrcaSlicer, 0.4m, "Generic PLA", requestedByUserId: null);
        var storage = new SequencedPartFileStorage(() => throw new UnauthorizedAccessException("permission denied"));
        var processor = new StubPartFileProcessor();
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
            requiresSlicing: true, SlicerEngine.OrcaSlicer, 0.4m, "Generic PLA", requestedByUserId: null);
        // First OpenReadAsync call (milestone 1) succeeds; second (milestone 2's estimate
        // continuation, since ImmediateEstimate is null below) throws a raw, unwrapped IOException —
        // exactly what LocalDiskPartFileStorage.OpenReadAsync's bare File.OpenRead can produce for a
        // misconfigured PartFileStorage:RootDirectory.
        var storage = new SequencedPartFileStorage(
            () => new MemoryStream([1, 2, 3]),
            () => throw new IOException("disk read failed"));
        var processor = new StubPartFileProcessor
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
        services.AddSingleton<IPartProcessingJobRepository>(new InMemoryPartProcessingJobRepository(job));
        services.AddSingleton<IPartRepository>(new EmptyPartRepository());
        services.AddSingleton(storage);
        services.AddSingleton(processor);
        services.AddSingleton(publisher);
        services.AddSingleton<IActivityLogWriter>(writer);

        // PartProcessingBackgroundService resolves these to populate tenant context per scope
        // (Finbuckle's HTTP-middleware-based resolution never runs for a scope it creates itself) —
        // mirrors PrinterConnectionBackgroundServiceTests' setup exactly.
        var tenantInfo = new AppTenantInfo { Id = job.OrganizationId.ToString(), Identifier = OrganizationTenantStore.StaticIdentifier };
        services.AddSingleton<IMultiTenantStore<AppTenantInfo>>(new StubTenantStore(tenantInfo));
        services.AddSingleton<AsyncLocalMultiTenantContextAccessor<AppTenantInfo>>();
        services.AddSingleton<IMultiTenantContextSetter>(sp => sp.GetRequiredService<AsyncLocalMultiTenantContextAccessor<AppTenantInfo>>());

        return services.BuildServiceProvider();
    }

    private sealed class InMemoryPartProcessingJobRepository(PartProcessingJob seedJob) : IPartProcessingJobRepository
    {
        public Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == seedJob.Id ? seedJob : null);

        public Task<IReadOnlyList<PartProcessingJob>> ListOrphanedInFlightAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PartProcessingJob>>([]);

        // The job is mutated in place (shared reference), so there's nothing to flush.
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyPartRepository : IPartRepository
    {
        public Task AddAsync(Part part, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Part?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Part?>(null);

        public Task<(IReadOnlyList<Part> Parts, int TotalCount)> ListAsync(
            IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyList<Part>, int)>(([], 0));

        public Task<IReadOnlyList<Part>> ListWithResolvingEstimateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Part>>([]);

        public Task RemoveAsync(Part part, CancellationToken cancellationToken) => Task.CompletedTask;

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

    private sealed class StubPartFileProcessor : IPartFileProcessor
    {
        public PartMaterialResolution MaterialResolution { get; set; } = new(0.4m, [], PlateCount: 1, ImmediateEstimate: null);

        public Task<PartMaterialResolution> ResolveMaterialAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken) =>
            Task.FromResult(MaterialResolution);

        public Task<PartEstimateResolution> ResolveEstimateAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("ResolveEstimateAsync should not be reached — the storage read ahead of it should have already thrown.");
    }

    private sealed class RecordingPartEventPublisher : IPartEventPublisher
    {
        public List<PartProcessingJob> StatusChanges { get; } = [];

        // job is a mutable reference type shared across every publish call, so re-reading .Status
        // off a StatusChanges entry after the fact only ever reflects the *current* status, not what
        // it was at publish time — snapshot the enum value (itself immutable) at each call instead.
        public List<PartProcessingJobStatus> StatusSnapshots { get; } = [];

        public Task PartProcessingJobStatusChangedAsync(Guid organizationId, PartProcessingJob job, CancellationToken cancellationToken)
        {
            StatusChanges.Add(job);
            StatusSnapshots.Add(job.Status);
            return Task.CompletedTask;
        }

        public Task PartUpsertedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PartEstimateResolvedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PartEstimateUnavailableAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;

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
