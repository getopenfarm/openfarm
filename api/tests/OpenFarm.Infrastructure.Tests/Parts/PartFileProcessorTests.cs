using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Test-after (research.md §12) — pure dispatch/orchestration against fakes for each dependency.
public class PartFileProcessorTests
{
    [Fact]
    public async Task ResolveMaterialAsync_AlreadySlicedFile_ReturnsImmediateEstimate()
    {
        var slicedParser = new FakeAlreadySlicedFileMetadataParser(new SlicedFileMetadata(
            0.4m, 47, 18.4m, 1, [new DetectedFilamentSlot("PETG", "#000000", null)], []));
        var processor = new PartFileProcessor(slicedParser, new FakeUnslicedProjectMaterialResolver(null), new FakeSlicerEngine());
        var job = PartProcessingJob.CreateAdd(PartFileFormat.GCode, "a.gcode", "path", 10, requiresSlicing: false, null, null, null, null, null);

        var result = await processor.ResolveMaterialAsync(job, new MemoryStream(), CancellationToken.None);

        result.NozzleDiameterMm.Should().Be(0.4m);
        result.ImmediateEstimate.Should().NotBeNull();
        result.ImmediateEstimate!.EstimatedPrintTimeMinutes.Should().Be(47);
        result.FilamentRequirements.Should().ContainSingle(r => r.MaterialType == "PETG");
    }

    [Fact]
    public async Task ResolveMaterialAsync_Stl_ProducesUnconstrainedWildcardWithNoImmediateEstimate()
    {
        var processor = new PartFileProcessor(
            new FakeAlreadySlicedFileMetadataParser(null), new FakeUnslicedProjectMaterialResolver(null), new FakeSlicerEngine());
        var job = PartProcessingJob.CreateAdd(
            PartFileFormat.Stl, "a.stl", "path", 10, requiresSlicing: true, SlicerEngine.OrcaSlicer, "2.2.0", 0.4m, "Generic PLA", null);

        var result = await processor.ResolveMaterialAsync(job, new MemoryStream(), CancellationToken.None);

        result.NozzleDiameterMm.Should().BeNull();
        result.EstimateUnavailableReason.Should().Contain("default estimation profiles");
        result.ImmediateEstimate.Should().BeNull();
        result.FilamentRequirements.Should().ContainSingle();
        result.FilamentRequirements[0].MaterialType.Should().BeNull();
    }

    [Fact]
    public async Task ResolveMaterialAsync_UnslicedThreeMf_ResolvesFromProjectSettings()
    {
        var processor = new PartFileProcessor(
            new FakeAlreadySlicedFileMetadataParser(null),
            new FakeUnslicedProjectMaterialResolver(new UnslicedProjectMaterial("PETG", 2, HasEmbeddedSettings: true, NozzleDiameterMm: 0.6m)),
            new FakeSlicerEngine());
        var job = PartProcessingJob.CreateAdd(
            PartFileFormat.ThreeMf, "a.3mf", "path", 10, requiresSlicing: true, SlicerEngine.OrcaSlicer, "2.2.0", 0.4m, "PETG", null);

        var result = await processor.ResolveMaterialAsync(job, new MemoryStream(), CancellationToken.None);

        result.FilamentRequirements.Should().ContainSingle(r => r.MaterialType == "PETG");
        result.PlateCount.Should().Be(2);
        result.ImmediateEstimate.Should().BeNull();
    }

    [Fact]
    public async Task ResolveEstimateAsync_DelegatesToSlicerEngine()
    {
        var processor = new PartFileProcessor(
            new FakeAlreadySlicedFileMetadataParser(null), new FakeUnslicedProjectMaterialResolver(null), new FakeSlicerEngine());
        var job = PartProcessingJob.CreateAdd(
            PartFileFormat.Stl, "a.stl", "path", 10, requiresSlicing: true, SlicerEngine.OrcaSlicer, "2.2.0", 0.4m, "Generic PLA", null);

        job.SetEstimationConfiguration(System.Text.Json.JsonSerializer.Serialize(new EstimationConfiguration(true, 0.4m, "PLA", null)));
        var result = await processor.ResolveEstimateAsync(job, new MemoryStream(), CancellationToken.None);

        result.EstimatedPrintTimeMinutes.Should().BeGreaterThan(0);
        result.Thumbnails.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResolveEstimateAsync_EmbeddedSettingsRejectedAndNoProjectPresets_KeepsTheSlicersFailure()
    {
        var processor = new PartFileProcessor(
            new FakeAlreadySlicedFileMetadataParser(null), new FakeUnslicedProjectMaterialResolver(null), new RejectingSlicerEngine());
        var job = PartProcessingJob.CreateAdd(
            PartFileFormat.ThreeMf, "a.3mf", "path", 10, requiresSlicing: true, SlicerEngine.OrcaSlicer, "2.3.1", null, "PETG", null);
        job.SetEstimationConfiguration(System.Text.Json.JsonSerializer.Serialize(new EstimationConfiguration(true, 0.4m, "PETG", null)));

        var act = () => processor.ResolveEstimateAsync(job, new MemoryStream(), CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingFailedException>()).Which.ExitCode.Should().Be(205);
    }

    private sealed class RejectingSlicerEngine : ISlicerEngine
    {
        public Task<SlicingResult> EstimateAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
            EstimationConfiguration configuration, CancellationToken cancellationToken) =>
            throw new SlicingFailedException("rejected", exitCode: 205);

        public Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
            string materialType, decimal nozzleDiameterMm, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAlreadySlicedFileMetadataParser(SlicedFileMetadata? result) : IAlreadySlicedFileMetadataParser
    {
        public Task<SlicedFileMetadata> ParseAsync(Stream fileStream, PartFileFormat format, CancellationToken cancellationToken) =>
            result is not null ? Task.FromResult(result) : throw new PartFileParsingFailedException("not configured");
    }

    private sealed class FakeUnslicedProjectMaterialResolver(UnslicedProjectMaterial? result) : IUnslicedProjectMaterialResolver
    {
        public Task<UnslicedProjectMaterial> ResolveAsync(Stream fileStream, CancellationToken cancellationToken) =>
            result is not null ? Task.FromResult(result) : throw new UnslicedProjectMaterialParsingFailedException("not configured");
    }
}
