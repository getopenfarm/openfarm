using System.Text.Json;
using OpenFarm.Application.Settings;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Parts;

public sealed class PartFileProcessor(
    IAlreadySlicedFileMetadataParser slicedMetadataParser,
    IUnslicedProjectMaterialResolver unslicedProjectMaterialResolver,
    ISlicerEngine slicerEngine,
    ResolveSlicerSelection? slicerSelection = null,
    IHostPlatformProvider? hostPlatform = null,
    ResolveProjectEstimationProfiles? projectProfiles = null) : IPartFileProcessor
{
    public async Task<PartMaterialResolution> ResolveMaterialAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken)
    {
        if (!job.RequiresSlicing)
        {
            var metadata = await slicedMetadataParser.ParseAsync(fileStream, job.FileFormat, cancellationToken);
            var requirements = PreviewFilamentRequirement.WithGrams(FilamentRequirementGrouper.Group(metadata.DetectedFilamentSlots), metadata.FilamentGrams);
            return new PartMaterialResolution(metadata.NozzleDiameterMm,
                requirements, FilamentRequirementGrouper.Plates(requirements, metadata.Plates),
                new(metadata.EstimatedPrintTimeMinutes, metadata.EstimatedFilamentWeightGrams, metadata.Thumbnails),
                MaterialMetadataComplete: metadata.MaterialMetadataComplete,
                EstimateUnavailableReason: PartEstimateAvailability.MissingMetadataReason(metadata.EstimatedPrintTimeMinutes, metadata.EstimatedFilamentWeightGrams),
                Estimation: metadata.Estimation ?? new("EmbeddedSlicedMetadata"), MetadataWarnings: metadata.Warnings);
        }

        var project = job.FileFormat == PartFileFormat.ThreeMf
            ? await unslicedProjectMaterialResolver.ResolveAsync(fileStream, cancellationToken) : new UnslicedProjectMaterial(null, [new FilePlate(1, null, [])]);
        if (project.HasEmbeddedSettings)
        {
            var configuration = new EstimationConfiguration(true, project.NozzleDiameterMm, project.MaterialType, null);
            var reason = project.EstimationUnavailableReason ?? AvailabilityReason(job, embedded: true);
            IReadOnlyList<PreviewFilamentRequirement> requirements = project.FilamentSlots is { Count: > 0 } slots ? FilamentRequirementGrouper.Group(slots)
                : [new(0, project.MaterialType, null, null)];
            return new(project.NozzleDiameterMm,
                requirements, FilamentRequirementGrouper.Plates(requirements, project.Plates), null, project.MaterialType,
                project.MaterialMetadataComplete, reason, project.Thumbnails, project.Estimation ?? new("EmbeddedProject"), configuration);
        }

        IReadOnlyList<PreviewFilamentRequirement> wildcard = [new(0, null, null, null)];
        return new(null, wildcard, FilamentRequirementGrouper.Plates(wildcard, project.Plates), null,
            EstimateUnavailableReason: "Estimation unavailable: this file does not contain embedded slicing settings or estimates.",
            Thumbnails: project.Thumbnails);

    }

    private string? AvailabilityReason(PartProcessingJob job, bool embedded)
    {
        if (job.SlicerEngineUsed is not { } engine || job.SlicerVersionUsed is not { } version)
            return "Estimation unavailable: no selected slicer is available.";
        if (embedded && engine is not (SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio))
            return "Estimation unavailable: this slicer does not support the embedded project format.";
        if (embedded && engine == SlicerEngine.BambuStudio && version == "2.7.1" && hostPlatform?.Current == SlicerPlatform.MacOs)
            return "Estimation unavailable: Bambu Studio 2.7.1 on macOS does not support reliable embedded-project estimation.";
        try { slicerSelection?.EnsureAvailable(new(engine, version)); }
        catch (SlicerUnavailableException ex) { return $"Estimation unavailable: {ex.Message}"; }
        return null;
    }

    public async Task<PartEstimateResolution> ResolveEstimateAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken)
    {
        var configuration = job.EstimationConfigurationJson is { } json ? JsonSerializer.Deserialize<EstimationConfiguration>(json) : null;
        if (configuration is null) throw new SlicingFailedException("No estimation configuration was resolved for this job.");
        var pin = new SlicerRef(job.SlicerEngineUsed!.Value, job.SlicerVersionUsed!);
        EstimationDetails? details = null;
        SlicingResult result;
        try
        {
            result = await slicerEngine.EstimateAsync(pin, fileStream, job.FileFormat, configuration, cancellationToken);
        }
        catch (SlicingFailedException ex) when (configuration.EmbeddedProject && SlicerCliExitCodes.RejectsProjectSettings(ex.ExitCode)
            && projectProfiles is not null && job.Estimation is { } project && fileStream.CanSeek)
        {
            // The slicer refused the project's own settings (typically saved by a newer version): estimate with the presets it
            // names, as this slicer ships them. If those cannot be used either, report both reasons.
            try
            {
                (configuration, details) = await projectProfiles.ResolveAsync(project, pin, cancellationToken);
            }
            catch (SlicingProfileUnconfiguredException unusable)
            {
                throw new SlicingFailedException($"{ex.Message} The project's presets could not be used instead: {unusable.Message}", ex, ex.ExitCode);
            }

            fileStream.Position = 0;
            result = await slicerEngine.EstimateAsync(pin, fileStream, job.FileFormat, configuration, cancellationToken);
        }
        // The sliced output is what a print draws from the spools, so its per-filament usage (purge included) is the by-colour split.
        return new(result.EstimatedPrintTimeMinutes, result.EstimatedFilamentWeightGrams, result.Thumbnails,
            JobFilamentUsageReader.ReadAllPlates(result.OutputFileBytes), details, details is null ? null : configuration, result.Plates);
    }
}
