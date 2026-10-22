using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>One source of effective filament requirements for matching, diagnostics and dispatch.</summary>
public sealed class ProductionJobRequirements(IProductionRunRepository runs, IPartRequirementRepository requirements)
{
    public static Part? ResolvePart(PrintJob job, PartRequirement? requirement, Part? libraryPart) =>
        requirement is not null && requirement.Quantity is null && job.DispatchedAtUtc is null
            ? libraryPart : job.GetProductionPart();

    /// <summary>
    /// The grams one filament line of a print takes: what the job was already sliced to for this revision, else the part
    /// file's figure for that colour, else a one-colour part's total. Null when unknown.
    /// </summary>
    public static decimal? EstimatedGrams(Part part, JobFilamentRequirementSpec requirement, int requirementCount, PrintJob? job, string? revisionHash) =>
        (job is not null && job.FileRevisionHash == (revisionHash ?? part.RevisionHash)
            ? job.Filaments.FirstOrDefault(line => line.Requirement == requirement)?.EstimatedGrams : null)
        ?? part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == requirement.MaterialType && n.DetectedColorHex == requirement.DetectedColorHex)?.EstimatedGrams
        ?? (requirementCount == 1 ? part.EstimatedFilamentWeightGrams : null);

    public static IReadOnlyList<JobFilamentRequirementSpec> FromPart(Part part) => part.FilamentRequirements
        .Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex,
            r.NarrowFilamentVariantId, r.NarrowFilamentProfileId)).ToList();

    public static IReadOnlyList<JobFilamentRequirementSpec> Resolve(PrintJob job, Part part, RevisionMode mode) =>
        !job.HasFilamentRequirementOverride && job.DispatchedAtUtc is null && mode == RevisionMode.FollowLatest
            ? FromPart(part) : job.Filaments.Select(c => c.Requirement).ToList();

    public async Task<(IReadOnlyList<JobFilamentRequirementSpec> Requirements, string Source, string RevisionHash)> ResolveAsync(
        PrintJob job, Part part, CancellationToken ct)
    {
        var requirement = await requirements.GetByIdAsync(job.PartRequirementId, ct);
        if (job.ProductionSnapshotJson is not null && (requirement?.Quantity is not null || job.DispatchedAtUtc is not null))
            return (job.Filaments.Select(c => c.Requirement).ToList(),
                job.HasFilamentRequirementOverride ? "Job override" : "Job snapshot", job.FileRevisionHash);
        var run = requirement is null ? null : await runs.GetByRunLineIdAsync(requirement.RunLineId, ct);
        var mode = run?.Lines.FirstOrDefault(l => l.Id == requirement!.RunLineId)?.RevisionMode ?? RevisionMode.Snapshot;
        var latest = !job.HasFilamentRequirementOverride && job.DispatchedAtUtc is null && mode == RevisionMode.FollowLatest;
        return (Resolve(job, part, mode), job.HasFilamentRequirementOverride ? "Job override" : latest ? "Follow-latest part" : "Job snapshot",
            mode == RevisionMode.FollowLatest && job.DispatchedAtUtc is null ? part.RevisionHash : job.FileRevisionHash);
    }
}
