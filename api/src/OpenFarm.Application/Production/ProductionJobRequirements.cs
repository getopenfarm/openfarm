using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>One source of effective filament requirements for matching, diagnostics and dispatch.</summary>
public sealed class ProductionJobRequirements(IProductionRunRepository runs, IPartRequirementRepository requirements)
{
    public static IReadOnlyList<JobFilamentRequirementSpec> FromPart(Part part) => part.FilamentRequirements
        .Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex,
            r.NarrowFilamentVariantId, r.NarrowFilamentProfileId)).ToList();

    public static IReadOnlyList<JobFilamentRequirementSpec> Resolve(PrintJob job, Part part, RevisionMode mode) =>
        !job.HasFilamentRequirementOverride && job.DispatchedAtUtc is null && mode == RevisionMode.FollowLatest
            ? FromPart(part) : job.FilamentConsumption.Select(c => c.Requirement).ToList();

    public async Task<(IReadOnlyList<JobFilamentRequirementSpec> Requirements, string Source, string RevisionHash)> ResolveAsync(
        PrintJob job, Part part, CancellationToken ct)
    {
        var requirement = await requirements.GetByIdAsync(job.PartRequirementId, ct);
        var run = requirement is null ? null : await runs.GetByRunLineIdAsync(requirement.RunLineId, ct);
        var mode = run?.Lines.FirstOrDefault(l => l.Id == requirement!.RunLineId)?.RevisionMode ?? RevisionMode.Snapshot;
        var latest = !job.HasFilamentRequirementOverride && job.DispatchedAtUtc is null && mode == RevisionMode.FollowLatest;
        return (Resolve(job, part, mode), job.HasFilamentRequirementOverride ? "Job override" : latest ? "Follow-latest part" : "Job snapshot",
            mode == RevisionMode.FollowLatest && job.DispatchedAtUtc is null ? part.RevisionHash : job.FileRevisionHash);
    }
}
