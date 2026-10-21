using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Tests.Parts.Fakes;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Tests.Parts;

// A part's slicer pin changes only when the operator says so: replacing the file or reprocessing never moves a
// part to the farm default, and moving it is an explicit engine + version choice.
public class PartSlicerPinHandlerTests
{
    private static readonly SlicerRef Orca = new(SlicerEngine.OrcaSlicer, "2.2.0");
    private static readonly SlicerRef Bambu = new(SlicerEngine.BambuStudio, "2.7.1");

    private sealed class Fixture
    {
        public Fixture(SlicerRef? partPin, SlicerRef farmDefault, params AvailableSlicer[] bundled)
        {
            Part = Part.Create("Widget", PartFileFormat.Stl, true, "widget.stl", "widget.stl", 10, 0.4m,
                partPin?.Engine, partPin?.Version, EstimateStatus.Available, 10, 5m, 1, 1, "hash",
                [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
            var settings = FarmSettings.Create();
            settings.SetDefaultSlicer(farmDefault);
            Selection = new ResolveSlicerSelection(new FakeAvailableSlicerCatalog(bundled), new FakeHostPlatformProvider(SlicerPlatform.Linux));
            Settings = new FakeFarmSettingsRepository(settings);
            Parts = new InMemoryPartRepository(Part);
        }

        public Part Part { get; }
        public ResolveSlicerSelection Selection { get; }
        public FakeFarmSettingsRepository Settings { get; }
        public InMemoryPartRepository Parts { get; }
        public RecordingJobs Jobs { get; } = new();
        public RecordingDispatcher Dispatcher { get; } = new();
    }

    private static AvailableSlicer Bundled(SlicerRef slicer) => new(slicer.Engine, slicer.Version, SlicerPlatform.Linux, $"/bundle/{slicer.Engine}/{slicer.Version}");

    private sealed class RecordingJobs : IPartProcessingJobRepository
    {
        public List<PartProcessingJob> Added { get; } = [];
        public Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken) { Added.Add(job); return Task.CompletedTask; }
        public Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Added.FirstOrDefault(j => j.Id == id));
        public Task<IReadOnlyList<PartProcessingJob>> ListOrphanedInFlightAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PartProcessingJob>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingDispatcher : IPartProcessingDispatcher
    {
        public void Enqueue(PartProcessingWorkItem workItem)
        {
        }
    }

    private sealed class StubClassifier : IPartFileClassifier
    {
        public PartFileClassification Classify(Stream fileStream, string fileName) => new(PartFileFormat.Stl, RequiresSlicing: true);
    }

    private sealed class StubStorage : IPartFileStorage
    {
        public Task<string> SaveAsync(string contentHash, string originalFileName, Stream content, CancellationToken cancellationToken) => Task.FromResult($"{contentHash}.stl");
        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static ReprocessPartCommandHandler Reprocess(Fixture f) => new(f.Parts, f.Jobs, f.Dispatcher, f.Settings, f.Selection);

    private static ReplacePartFileCommandHandler Replace(Fixture f) =>
        new(new StubClassifier(), new StubStorage(), f.Parts, f.Jobs, f.Dispatcher, f.Settings, f.Selection);

    private static ReplacePartFileCommand ReplaceCommand(Guid partId, SlicerEngine? engine = null, string? version = null) =>
        new(partId, new MemoryStream([1, 2, 3]), "widget.stl", engine, version, null);

    [Fact]
    public async Task Reprocess_AskingForNothing_KeepsThePartsPinNotTheFarmDefault()
    {
        var f = new Fixture(Orca, farmDefault: Bambu, Bundled(Orca), Bundled(Bambu));

        await Reprocess(f).HandleAsync(new ReprocessPartCommand(f.Part.Id, null), default);

        f.Jobs.Added.Should().ContainSingle().Which.Should().Match<PartProcessingJob>(j => j.SlicerEngineUsed == Orca.Engine && j.SlicerVersionUsed == Orca.Version);
    }

    [Fact]
    public async Task Reprocess_WithAnExplicitSlicer_MovesThePartToIt()
    {
        var f = new Fixture(Orca, farmDefault: Orca, Bundled(Orca), Bundled(Bambu));

        await Reprocess(f).HandleAsync(new ReprocessPartCommand(f.Part.Id, null, Bambu.Engine, Bambu.Version), default);

        f.Jobs.Added.Should().ContainSingle().Which.Should().Match<PartProcessingJob>(j => j.SlicerEngineUsed == Bambu.Engine && j.SlicerVersionUsed == Bambu.Version);
    }

    [Fact]
    public async Task Reprocess_PinnedSlicerNoLongerBundled_KeepsThePairSoTheFailureNamesIt()
    {
        var f = new Fixture(Orca, farmDefault: Bambu, Bundled(Bambu));

        await Reprocess(f).HandleAsync(new ReprocessPartCommand(f.Part.Id, null), default);

        f.Jobs.Added.Should().ContainSingle().Which.Should().Match<PartProcessingJob>(j => j.SlicerEngineUsed == Orca.Engine && j.SlicerVersionUsed == Orca.Version);
    }

    [Fact]
    public async Task ReplaceFile_AskingForNothing_KeepsThePartsPinNotTheFarmDefault()
    {
        var f = new Fixture(Orca, farmDefault: Bambu, Bundled(Orca), Bundled(Bambu));

        await Replace(f).HandleAsync(ReplaceCommand(f.Part.Id), default);

        f.Jobs.Added.Should().ContainSingle().Which.Should().Match<PartProcessingJob>(j => j.SlicerEngineUsed == Orca.Engine && j.SlicerVersionUsed == Orca.Version);
    }

    [Fact]
    public async Task ReplaceFile_WithAnExplicitSlicer_MovesThePartToIt()
    {
        var f = new Fixture(Orca, farmDefault: Orca, Bundled(Orca), Bundled(Bambu));

        await Replace(f).HandleAsync(ReplaceCommand(f.Part.Id, Bambu.Engine, Bambu.Version), default);

        f.Jobs.Added.Should().ContainSingle().Which.Should().Match<PartProcessingJob>(j => j.SlicerEngineUsed == Bambu.Engine && j.SlicerVersionUsed == Bambu.Version);
    }

    [Fact]
    public async Task ReplaceFile_PartWithoutAPin_TakesTheFarmDefault()
    {
        var f = new Fixture(partPin: null, farmDefault: Bambu, Bundled(Orca), Bundled(Bambu));

        await Replace(f).HandleAsync(ReplaceCommand(f.Part.Id), default);

        f.Jobs.Added.Should().ContainSingle().Which.Should().Match<PartProcessingJob>(j => j.SlicerEngineUsed == Bambu.Engine && j.SlicerVersionUsed == Bambu.Version);
    }
}
