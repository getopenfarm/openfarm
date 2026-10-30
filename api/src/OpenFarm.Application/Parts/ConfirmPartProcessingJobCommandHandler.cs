using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The Save action — creates (Add) or replaces the file/derived fields of (Replace) a
/// <see cref="Part"/> transactionally from whatever the job's preview holds so far
/// (data-model.md's Confirm step); an Add of several plates creates one part each.
/// Test-after — orchestration, not the domain rules themselves.
/// </summary>
public sealed class ConfirmPartProcessingJobCommandHandler(
    IPartProcessingJobRepository jobRepository,
    IPartRepository partRepository,
    ITagRepository tagRepository,
    IPartEventPublisher eventPublisher,
    IPartFileStorage? fileStorage = null, PartFilamentSelectionValidator? filamentValidator = null,
    IActivityLogWriter? activityLogWriter = null) : ICommandHandler<ConfirmPartProcessingJobCommand, Part>
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

        // One part per chosen plate: several only when adding a file splits it.
        var plates = ChoosePlates(job, command, existingPart);
        var related = await RelatedPartsAsync(job, command, existingPart, cancellationToken);
        var overridesByIndex = (command.FilamentRequirementOverrides ?? [])
            .ToDictionary(o => o.RequirementIndex);

        foreach (var index in overridesByIndex.Keys)
        {
            if (!job.PreviewFilamentRequirements.Any(r => r.RequirementIndex == index))
            {
                throw new InvalidConfirmPartProcessingJobRequestException($"requirementIndex {index} does not match a preview requirement.");
            }
        }

        var values = plates.Select(plate => ValuesOf(job, plate, overridesByIndex, existingPart)).ToList();
        var relatedValues = related.Select(r => ValuesOf(job, r.Plate, overridesByIndex, r.Part)).ToList();
        if (filamentValidator is not null)
            foreach (var selection in values.Concat(relatedValues).SelectMany(v => v.Requirements))
                await filamentValidator.ValidateAsync(selection.MaterialType, selection.NarrowFilamentVariantId, cancellationToken);

        // The file was already saved under a content-hash-keyed path (research.md R6) by the
        // Add/Replace submit step (AddPartCommandHandler/ReplacePartFileCommandHandler) — the hash
        // is the path's own filename stem, so it's recovered here rather than re-hashed.
        var revisionHash = Path.GetFileNameWithoutExtension(job.StoragePath);

        var parts = new List<Part>();
        if (job.Kind == PartProcessingJobKind.Add)
        {
            var tags = new List<Tag>();
            foreach (var tagName in command.Tags ?? [])
            {
                tags.Add(await GetOrCreateTagAsync(tagName, cancellationToken));
            }

            for (var i = 0; i < plates.Count; i++)
            {
                var (plate, value) = (plates[i], values[i]);
                var part = Part.Create(
                    plates.Count > 1 ? $"{command.Name!.Trim()} – {plate!.Name ?? $"Plate {plate.Number}"}" : command.Name!,
                    job.FileFormat,
                    job.RequiresSlicing,
                    job.OriginalFileName,
                    job.StoragePath,
                    job.FileSizeBytes,
                    job.PreviewNozzleDiameterMm,
                    job.SlicerEngineUsed,
                    job.SlicerVersionUsed,
                    value.Status,
                    value.Minutes,
                    value.Grams,
                    plate?.Number,
                    unitsPerPrint: fileStorage is not null && (!job.RequiresSlicing || plate is not null)
                        ? await SlicedFileUnitsPerPrint.SuggestAsync(fileStorage, job.StoragePath, plate?.Number, cancellationToken) : 1,
                    revisionHash,
                    value.Requirements,
                    value.Thumbnails,
                    tags,
                    plate?.Name);

                part.SetEstimation(job.Estimation);
                part.SetMetadataWarnings(job.MetadataWarnings);
                part.SetProcessingMetadata(job.Id, job.PreviewMaterialMetadataComplete, value.UnavailableReason);
                parts.Add(part);
            }

            foreach (var created in parts) created.MoveToFolder(command.FolderId);
            job.Confirm(parts[0].Id);
            await partRepository.AddRangeAsync(parts, cancellationToken);
            // The endpoint logs the first part; the others are logged here.
            if (activityLogWriter is not null && parts.Count > 1)
                await activityLogWriter.WriteRangeAsync(parts.Skip(1).Select(p => Entry("Part", p.Id, "PartConfirmed", command.ActingUserId)).ToList(),
                    cancellationToken);
        }
        else
        {
            // The replaced part, then the other parts sharing its file, each taking its own plate of the new file; one save.
            var part = await partRepository.GetByIdAsync(job.PartId!.Value, cancellationToken)
                ?? throw new PartNotFoundException(job.PartId!.Value);
            await UpdateAsync(job, part, plates[0], values[0], revisionHash, cancellationToken);
            for (var i = 0; i < related.Count; i++)
                await UpdateAsync(job, related[i].Part, related[i].Plate, relatedValues[i], revisionHash, cancellationToken);
            job.Confirm(part.Id);
            await partRepository.SaveChangesAsync(cancellationToken);
            parts.Add(part);
            parts.AddRange(related.Select(r => r.Part));
            if (activityLogWriter is not null && related.Count > 0)
                await activityLogWriter.WriteRangeAsync(related.Select(r => Entry("Part", r.Part.Id, "PartConfirmed", command.ActingUserId)).ToList(),
                    cancellationToken);
        }

        foreach (var part in parts)
            await eventPublisher.PartUpsertedAsync(part.Id, cancellationToken);

        return parts[0];
    }

    /// <summary>Gives an existing part the job's file (a replace) or its re-read results (a reprocess), printing <paramref name="plate"/>.</summary>
    private async Task UpdateAsync(PartProcessingJob job, Part part, PreviewPlate? plate, PlateValues value, string revisionHash,
        CancellationToken cancellationToken)
    {
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
                value.Status, value.Minutes, value.Grams, plate?.Number, value.Requirements, value.Thumbnails, plate?.Name);
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
            value.Status,
            value.Minutes,
            value.Grams,
            plate?.Number,
            revisionHash,
            value.Requirements,
            value.Thumbnails,
            plate?.Name);

        part.SetEstimation(job.Estimation);
        part.SetMetadataWarnings(job.MetadataWarnings);
        part.SetProcessingMetadata(job.Id, job.PreviewMaterialMetadataComplete, value.UnavailableReason);
        partRepository.TrackReplacementChildren(part);
    }

    /// <summary>
    /// The other parts a replace updates and the plate of the new file each prints. Each must still share the replaced part's
    /// file: one changed since the review is a conflict, not silently replaced. Only once the estimate is in (its continuation
    /// updates one part).
    /// </summary>
    private async Task<List<(Part Part, PreviewPlate Plate)>> RelatedPartsAsync(PartProcessingJob job, ConfirmPartProcessingJobCommand command,
        Part? replaced, CancellationToken cancellationToken)
    {
        if (command.RelatedParts is not { Count: > 0 } requested) return [];
        if (job.Kind != PartProcessingJobKind.Replace || replaced is null)
            throw new InvalidConfirmPartProcessingJobRequestException("Only a file replace updates other parts.");
        if (job.PreviewEstimateStatus == EstimateStatus.Resolving)
            throw new InvalidConfirmPartProcessingJobRequestException("Wait for the estimate to replace several parts.");
        if (requested.Select(r => r.PartId).Distinct().Count() != requested.Count || requested.Any(r => r.PartId == replaced.Id))
            throw new InvalidConfirmPartProcessingJobRequestException("Each related part may be named once.");

        var related = new List<(Part, PreviewPlate)>();
        foreach (var entry in requested)
        {
            var part = await partRepository.GetByIdAsync(entry.PartId, cancellationToken) ?? throw new PartNotFoundException(entry.PartId);
            if (part.RevisionHash != replaced.RevisionHash) throw new PartConcurrencyConflictException(part.Id);
            related.Add((part, job.PreviewPlates.FirstOrDefault(p => p.Number == entry.PlateNumber)
                ?? throw new InvalidConfirmPartProcessingJobRequestException($"Plate {entry.PlateNumber} is not in the file.")));
        }

        return related;
    }

    private static ActivityLogEntry Entry(string entityType, Guid id, string eventType, Guid? actor) =>
        ActivityLogEntry.Create(entityType, id.ToString(), eventType, actor, actor is null ? ActorType.System : ActorType.Operator);

    /// <summary>What a part printing <paramref name="plate"/> takes from the job (the whole file for a job without plates).</summary>
    private sealed record PlateValues(IReadOnlyList<FilamentRequirementSpec> Requirements, IReadOnlyList<ThumbnailSpec> Thumbnails,
        int? Minutes, decimal? Grams, EstimateStatus Status, string? UnavailableReason);

    private static PlateValues ValuesOf(PartProcessingJob job, PreviewPlate? plate,
        Dictionary<int, FilamentRequirementOverride> overrides, Part? existingPart)
    {
        var requirements = (plate is null ? job.PreviewFilamentRequirements : job.PlateRequirements(plate))
            .Select(preview =>
            {
                overrides.TryGetValue(preview.RequirementIndex, out var overrideEntry);
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

        var thumbnails = job.PreviewThumbnails
            .Where(t => plate is null || t.PlateIndex == plate.Number - 1)
            .Select(t => new ThumbnailSpec(t.PlateIndex, t.Bytes))
            .ToList();

        // A part prints one plate, so its estimate is that plate's (still Resolving while the slice runs). The file's
        // "missing metadata" reason describes its totals; the plate's own numbers decide it instead.
        var minutes = plate is null ? job.PreviewEstimatedPrintTimeMinutes : plate.EstimatedPrintTimeMinutes;
        var grams = plate is null ? job.PreviewEstimatedFilamentWeightGrams : plate.EstimatedFilamentWeightGrams;
        var status = plate is null || job.PreviewEstimateStatus == EstimateStatus.Resolving
            ? job.PreviewEstimateStatus!.Value : PartEstimateAvailability.From(minutes, grams);
        var reason = plate is not null && job.EstimateUnavailableReason == PartEstimateAvailability.MissingMetadataReason(null, null)
            ? PartEstimateAvailability.MissingMetadataReason(minutes, grams) : job.EstimateUnavailableReason;
        return new(requirements, thumbnails, minutes, grams, status, reason);
    }

    /// <summary>
    /// The plates the confirmation makes parts of: the one chosen, else the file's only plate, else the part's own plate when the
    /// file still has it (<see cref="Match"/>). Several make one part each, only when adding a file whose estimate is in (a continuation updates one
    /// part). <c>[null]</c> for a job processed before plates were recorded, which keeps a legacy part (plate 1, else the file's
    /// only sliced plate).
    /// </summary>
    private static List<PreviewPlate?> ChoosePlates(PartProcessingJob job, ConfirmPartProcessingJobCommand command, Part? current)
    {
        var plates = job.PreviewPlates;
        var chosen = command.PlateNumbers ?? [];
        PreviewPlate Find(int number) => plates.FirstOrDefault(p => p.Number == number)
            ?? throw new InvalidConfirmPartProcessingJobRequestException($"Plate {number} is not in the file.");

        if (chosen.Count > 1)
        {
            if (job.Kind != PartProcessingJobKind.Add)
                throw new InvalidConfirmPartProcessingJobRequestException("A part prints one plate. Choose one.");
            if (chosen.Distinct().Count() != chosen.Count)
                throw new InvalidConfirmPartProcessingJobRequestException("Each plate may be named once.");
            if (job.PreviewEstimateStatus == EstimateStatus.Resolving)
                throw new InvalidConfirmPartProcessingJobRequestException("Wait for the estimate to add several plates.");
            return chosen.Select(n => (PreviewPlate?)Find(n)).ToList();
        }

        if (plates.Count == 0) return [null];
        return [chosen.Count == 1 ? Find(chosen[0])
            : plates.Count == 1 ? plates[0]
            : Match(plates, current) ?? throw new InvalidConfirmPartProcessingJobRequestException("Choose a plate.")];
    }

    /// <summary>
    /// A part's plate in a (new) file: the plate with its name when no other plate has that name, else the plate with its
    /// number unless that plate is named otherwise — plate numbers shift when plates are reordered or removed in the slicer,
    /// names do not.
    /// </summary>
    private static PreviewPlate? Match(IReadOnlyList<PreviewPlate> plates, Part? part) =>
        part?.PlateName is { } name && plates.Where(p => p.Name == name).ToList() is [var named] ? named
            : plates.FirstOrDefault(p => p.Number == part?.PlateNumber && (p.Name is null || part.PlateName is null || p.Name == part.PlateName));

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
