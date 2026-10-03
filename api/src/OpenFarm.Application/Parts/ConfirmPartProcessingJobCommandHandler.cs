using OpenFarm.Application.Common;
using OpenFarm.Domain.Organizations;
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
    IPartEventPublisher eventPublisher) : ICommandHandler<ConfirmPartProcessingJobCommand, Part>
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

        var overridesByIndex = (command.FilamentRequirementOverrides ?? [])
            .ToDictionary(o => o.RequirementIndex);

        foreach (var index in overridesByIndex.Keys)
        {
            if (!job.PreviewFilamentRequirements.Any(r => r.RequirementIndex == index))
            {
                throw new InvalidConfirmPartProcessingJobRequestException($"requirementIndex {index} does not match a preview requirement.");
            }
        }

        var filamentRequirements = job.PreviewFilamentRequirements
            .Select(preview =>
            {
                overridesByIndex.TryGetValue(preview.RequirementIndex, out var overrideEntry);
                return new FilamentRequirementSpec(
                    preview.MaterialType,
                    preview.DetectedColorHex,
                    preview.DetectedSubBrand,
                    overrideEntry?.NarrowFilamentVariantId,
                    overrideEntry?.NarrowFilamentProfileId);
            })
            .ToList();

        var thumbnails = job.PreviewThumbnails
            .Select(t => new ThumbnailSpec(t.PlateIndex, t.Bytes))
            .ToList();

        var estimateStatus = job.PreviewEstimateStatus!.Value;

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
                Organization.WellKnownId,
                command.Name!,
                job.FileFormat,
                job.RequiresSlicing,
                job.OriginalFileName,
                job.StoragePath,
                job.FileSizeBytes,
                job.PreviewNozzleDiameterMm!.Value,
                job.SlicerEngineUsed,
                job.SlicerVersionUsed,
                estimateStatus,
                job.PreviewEstimatedPrintTimeMinutes,
                job.PreviewEstimatedFilamentWeightGrams,
                job.PreviewPlateCount!.Value,
                unitsPerPrint: 1,
                revisionHash,
                filamentRequirements,
                thumbnails,
                tags);

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
            part.ReplaceFile(
                job.FileFormat,
                job.RequiresSlicing,
                job.OriginalFileName,
                job.StoragePath,
                job.FileSizeBytes,
                job.PreviewNozzleDiameterMm!.Value,
                job.SlicerEngineUsed,
                job.SlicerVersionUsed,
                estimateStatus,
                job.PreviewEstimatedPrintTimeMinutes,
                job.PreviewEstimatedFilamentWeightGrams,
                job.PreviewPlateCount!.Value,
                revisionHash,
                filamentRequirements,
                thumbnails);

            await partRepository.SaveChangesAsync(cancellationToken);
        }

        job.Confirm(part.Id);
        await jobRepository.SaveChangesAsync(cancellationToken);

        await eventPublisher.PartUpsertedAsync(part.OrganizationId, part.Id, cancellationToken);

        return part;
    }

    private async Task<Tag> GetOrCreateTagAsync(string name, CancellationToken cancellationToken)
    {
        var existing = await tagRepository.FindByNameAsync(name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var tag = Tag.Create(Organization.WellKnownId, name);
        await tagRepository.AddAsync(tag, cancellationToken);
        return tag;
    }
}
