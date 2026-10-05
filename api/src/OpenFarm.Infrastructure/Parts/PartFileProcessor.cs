using System.Text.Json;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

public sealed class PartFileProcessor(
    IAlreadySlicedFileMetadataParser slicedMetadataParser,
    IUnslicedProjectMaterialResolver unslicedProjectMaterialResolver,
    ISlicerEngine slicerEngine,
    ResolveEstimationProfiles? estimationProfiles = null,
    ResolveSlicerSelection? slicerSelection = null,
    IOrganizationRepository? organizations = null, IHostPlatformProvider? hostPlatform = null) : IPartFileProcessor
{
    public async Task<PartMaterialResolution> ResolveMaterialAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken)
    {
        if (!job.RequiresSlicing)
        {
            var metadata = await slicedMetadataParser.ParseAsync(fileStream, job.FileFormat, cancellationToken);
            return new PartMaterialResolution(metadata.NozzleDiameterMm,
                FilamentRequirementGrouper.Group(metadata.DetectedFilamentSlots), metadata.PlateCount,
                new(metadata.EstimatedPrintTimeMinutes, metadata.EstimatedFilamentWeightGrams, metadata.Thumbnails),
                MaterialMetadataComplete: metadata.MaterialMetadataComplete,
                EstimateUnavailableReason: PartEstimateAvailability.MissingMetadataReason(metadata.EstimatedPrintTimeMinutes, metadata.EstimatedFilamentWeightGrams),
                Estimation: metadata.Estimation ?? new("EmbeddedSlicedMetadata"), MetadataWarnings: metadata.Warnings);
        }

        var project = job.FileFormat == PartFileFormat.ThreeMf
            ? await unslicedProjectMaterialResolver.ResolveAsync(fileStream, cancellationToken) : new UnslicedProjectMaterial(null, 1);
        if (project.HasEmbeddedSettings)
        {
            var configuration = new EstimationConfiguration(true, project.NozzleDiameterMm, project.MaterialType, null);
            var reason = project.EstimationUnavailableReason ?? AvailabilityReason(job, embedded: true);
            return new(project.NozzleDiameterMm,
                project.FilamentSlots is { Count: > 0 } slots ? FilamentRequirementGrouper.Group(slots)
                    : [new(0, project.MaterialType, null, null)], project.PlateCount, null, project.MaterialType,
                project.MaterialMetadataComplete, reason, project.Thumbnails, project.Estimation ?? new("EmbeddedProject"), configuration);
        }

        EstimationConfiguration? defaults = null;
        EstimationDetails details = new("DefaultProfiles");
        string? unavailable = null;
        try
        {
            var organization = organizations is null ? null : await organizations.GetAsync(cancellationToken);
            var selection = organization is null ? null : UpdateSlicingDefaultsCommandHandler.GetSelection(organization);
            if (selection is null || estimationProfiles is null)
                unavailable = "Estimation unavailable: select default estimation profiles in Settings → Slicing Defaults.";
            else if (job.SlicerEngineUsed is { } engine)
                (defaults, details) = await estimationProfiles.ResolveAsync(selection, engine, cancellationToken);
            unavailable ??= AvailabilityReason(job, embedded: false);
        }
        catch (SlicingProfileUnconfiguredException ex) { unavailable = $"Estimation unavailable: {ex.Message}"; }
        return new(defaults?.NozzleDiameterMm,
            [new(0, null, null, null)], project.PlateCount, null, defaults?.MaterialType,
            EstimateUnavailableReason: unavailable, Thumbnails: project.Thumbnails, Estimation: details, EstimationConfiguration: defaults);
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
        var result = await slicerEngine.EstimateAsync(new(job.SlicerEngineUsed!.Value, job.SlicerVersionUsed!),
            fileStream, job.FileFormat, configuration, cancellationToken);
        return new(result.EstimatedPrintTimeMinutes, result.EstimatedFilamentWeightGrams, result.Thumbnails);
    }
}
