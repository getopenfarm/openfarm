using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The Save action — creates (Add) or replaces the file/derived fields of (Replace) a
/// <see cref="Part"/> transactionally from whatever the job's preview holds so far
/// (data-model.md's Confirm step). Test-after — orchestration, not the domain rules themselves.
/// </summary>
public sealed class ConfirmPartProcessingJobCommandHandler(
    IPartProcessingJobRepository jobRepository,
    IPartRepository partRepository,
    ITagRepository tagRepository,
    IPartEventPublisher eventPublisher,
    IPartFileStorage? fileStorage = null, PartFilamentSelectionValidator? filamentValidator = null) : ICommandHandler<ConfirmPartProcessingJobCommand, Part>
{
    public async Task<Part> HandleAsync(ConfirmPartProcessingJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PartProcessingJobNotFoundException(command.JobId);

        if (job.Status != PartProcessingJobStatus.ReadyForReview)
        {
            throw new PartProcessingJobNotReadyForReviewException(job.Id);
        }

        if (job.Kind == PartProcessingJobKind.Add)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                throw new InvalidConfirmPartProcessingJobRequestException("Name is required to confirm a new part.");
            }
        }
        else if (command.Name is not null || command.Tags is not null)
        {
            throw new InvalidConfirmPartProcessingJobRequestException("A replace confirmation must not include name or tags.");
        }

        Part? existingPart = null;
        if (job.Kind != PartProcessingJobKind.Add)
        {
            existingPart = await partRepository.GetByIdAsync(job.PartId!.Value, cancellationToken)
                ?? throw new PartNotFoundException(job.PartId.Value);
            if (job.Kind == PartProcessingJobKind.Reprocess && (existingPart.RevisionHash != job.SourceRevisionHash || existingPart.UpdatedAtUtc != job.SourcePartUpdatedAtUtc))
                throw new PartConcurrencyConflictException(existingPart.Id);
        }

        var plate = ChoosePlate(job, command.PlateNumbers, existingPart?.PlateNumber);
        var overridesByIndex = (command.FilamentRequirementOverrides ?? [])
            .ToDictionary(o => o.RequirementIndex);

        foreach (var index in overridesByIndex.Keys)
        {
            if (!job.PreviewFilamentRequirements.Any(r => r.RequirementIndex == index))
            {
                throw new InvalidConfirmPartProcessingJobRequestException($"requirementIndex {index} does not match a preview requirement.");
            }
        }

        var filamentRequirements = (plate is null ? job.PreviewFilamentRequirements : job.PlateRequirements(plate))
            .Select(preview =>
            {
                overridesByIndex.TryGetValue(preview.RequirementIndex, out var overrideEntry);
                var previous = existingPart?.FilamentRequirements.FirstOrDefault(r => r.MaterialType == preview.MaterialType
                    && r.DetectedColorHex == preview.DetectedColorHex && (r.DetectedSubBrand == preview.DetectedSubBrand || r.DetectedSubBrand is null));
                return new FilamentRequirementSpec(
                    preview.MaterialType,
                    preview.DetectedColorHex,
                    preview.DetectedSubBrand,
                    overrideEntry is not null ? overrideEntry.NarrowFilamentVariantId : preview.NarrowFilamentVariantId ?? previous?.NarrowFilamentVariantId,
                    overrideEntry is not null ? overrideEntry.NarrowFilamentProfileId : preview.NarrowFilamentProfileId ?? previous?.NarrowFilamentProfileId,
                    preview.EstimatedGrams);
            })
            .ToList();

        if (filamentValidator is not null)
            foreach (var selection in filamentRequirements)
                await filamentValidator.ValidateAsync(selection.MaterialType, selection.NarrowFilamentVariantId, cancellationToken);

        var thumbnails = job.PreviewThumbnails
            .Where(t => plate is null || t.PlateIndex == plate.Number - 1)
            .Select(t => new ThumbnailSpec(t.PlateIndex, t.Bytes))
            .ToList();

        // A part prints one plate, so its estimate is that plate's (still Resolving while the slice runs). The file's
        // "missing metadata" reason describes its totals; the plate's own numbers decide it instead.
        var minutes = plate is null ? job.PreviewEstimatedPrintTimeMinutes : plate.EstimatedPrintTimeMinutes;
        var grams = plate is null ? job.PreviewEstimatedFilamentWeightGrams : plate.EstimatedFilamentWeightGrams;
        var estimateStatus = plate is null || job.PreviewEstimateStatus == EstimateStatus.Resolving
            ? job.PreviewEstimateStatus!.Value : PartEstimateAvailability.From(minutes, grams);
        var unavailableReason = plate is not null && job.EstimateUnavailableReason == PartEstimateAvailability.MissingMetadataReason(null, null)
            ? PartEstimateAvailability.MissingMetadataReason(minutes, grams) : job.EstimateUnavailableReason;

        // The file was already saved under a content-hash-keyed path (research.md R6) by the
        // Add/Replace submit step (AddPartCommandHandler/ReplacePartFileCommandHandler) — the hash
        // is the path's own filename stem, so it's recovered here rather than re-hashed.
        var revisionHash = Path.GetFileNameWithoutExtension(job.StoragePath);

        Part part;
        if (job.Kind == PartProcessingJobKind.Add)
        {
            var tags = new List<Tag>();
            foreach (var tagName in command.Tags ?? [])
            {
                tags.Add(await GetOrCreateTagAsync(tagName, cancellationToken));
            }

            part = Part.Create(
                    command.Name!,
                job.FileFormat,
                job.RequiresSlicing,
                job.OriginalFileName,
                job.StoragePath,
                job.FileSizeBytes,
                job.PreviewNozzleDiameterMm,
                job.SlicerEngineUsed,
                job.SlicerVersionUsed,
                estimateStatus,
                minutes,
                grams,
                plate?.Number,
                unitsPerPrint: fileStorage is not null && (!job.RequiresSlicing || plate is not null)
                    ? await SlicedFileUnitsPerPrint.SuggestAsync(fileStorage, job.StoragePath, plate?.Number, cancellationToken) : 1,
                revisionHash,
                filamentRequirements,
                thumbnails,
                tags,
                plate?.Name);

            part.SetEstimation(job.Estimation);
            part.SetMetadataWarnings(job.MetadataWarnings);
            part.SetProcessingMetadata(job.Id, job.PreviewMaterialMetadataComplete, unavailableReason);
            job.Confirm(part.Id);
            await partRepository.AddAsync(part, cancellationToken);
        }
        else
        {
            part = await partRepository.GetByIdAsync(job.PartId!.Value, cancellationToken)
                ?? throw new PartNotFoundException(job.PartId!.Value);

            // research.md R6 — replace no longer deletes the old revision's bytes: a Snapshot-mode
            // PrintJob elsewhere may still reference them by their own RevisionHash. Content
            // addressing means an unreferenced revision is simply left on disk (no reaper yet,
            // spec Assumptions), never eagerly reclaimed here.
            // Older installations stored uploads under an owner ID. Preserve the old revision
            // alias before changing the Part so queued Snapshot jobs still resolve its bytes.
            if (fileStorage is not null && Path.GetFileNameWithoutExtension(part.StoragePath) != part.RevisionHash)
            {
                await using var oldFile = await fileStorage.OpenReadAsync(part.StoragePath, cancellationToken);
                await fileStorage.SaveAsync(part.RevisionHash, part.OriginalFileName, oldFile, cancellationToken);
            }
            if (job.Kind == PartProcessingJobKind.Reprocess)
            {
                part.Reprocess(job.PreviewNozzleDiameterMm, job.SlicerEngineUsed, job.SlicerVersionUsed,
                    estimateStatus, minutes, grams, plate?.Number, filamentRequirements, thumbnails, plate?.Name);
            }
            else part.ReplaceFile(
                job.FileFormat,
                job.RequiresSlicing,
                job.OriginalFileName,
                job.StoragePath,
                job.FileSizeBytes,
                job.PreviewNozzleDiameterMm,
                job.SlicerEngineUsed,
                job.SlicerVersionUsed,
                estimateStatus,
                minutes,
                grams,
                plate?.Number,
                revisionHash,
                filamentRequirements,
                thumbnails,
                plate?.Name);

            part.SetEstimation(job.Estimation);
            part.SetMetadataWarnings(job.MetadataWarnings);
            part.SetProcessingMetadata(job.Id, job.PreviewMaterialMetadataComplete, unavailableReason);
            partRepository.TrackReplacementChildren(part);
            job.Confirm(part.Id);
            await partRepository.SaveChangesAsync(cancellationToken);
        }


        await eventPublisher.PartUpsertedAsync(part.Id, cancellationToken);

        return part;
    }

    /// <summary>
    /// The plate the part prints: the one chosen, else the file's only plate, else the part's own plate when the file still has
    /// it. <c>null</c> for a job processed before plates were recorded, which keeps a legacy part (plate 1, else the file's only
    /// sliced plate).
    /// </summary>
    private static PreviewPlate? ChoosePlate(PartProcessingJob job, IReadOnlyList<int>? chosen, int? current)
    {
        var plates = job.PreviewPlates;
        if (plates.Count == 0) return null;
        return chosen switch
        {
            null or [] => plates.Count == 1 ? plates[0] : plates.FirstOrDefault(p => p.Number == current)
                ?? throw new InvalidConfirmPartProcessingJobRequestException("Choose a plate."),
            [var number] => plates.FirstOrDefault(p => p.Number == number)
                ?? throw new InvalidConfirmPartProcessingJobRequestException($"Plate {number} is not in the file."),
            _ => throw new InvalidConfirmPartProcessingJobRequestException("A part prints one plate. Choose one."),
        };
    }

    private async Task<Tag> GetOrCreateTagAsync(string name, CancellationToken cancellationToken)
    {
        var existing = await tagRepository.FindByNameAsync(name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var tag = Tag.Create(name);
        await tagRepository.AddAsync(tag, cancellationToken);
        return tag;
    }
}
