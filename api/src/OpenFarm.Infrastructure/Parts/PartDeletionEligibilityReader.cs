using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>The same policy serves advisory previews and locked deletion.</summary>
public sealed class PartDeletionEligibilityReader(OpenFarmDbContext db) : IPartDeletionEligibilityReader
{
    public async Task<IReadOnlyList<PartDeletionEligibility>> CheckAsync(IReadOnlyList<Guid> partIds, CancellationToken cancellationToken)
    {
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id, cancellationToken);
        var assemblies = await (from line in db.BomLines.AsNoTracking()
                                join assembly in db.Assemblies on line.AssemblyId equals assembly.Id
                                where line.TargetPartId != null && partIds.Contains(line.TargetPartId.Value)
                                select new { PartId = line.TargetPartId!.Value, assembly.Id, assembly.Name })
            .Distinct().ToListAsync(cancellationToken);
        var unlimited = await (from requirement in db.PartRequirements.AsNoTracking()
                               join line in db.RunLines on requirement.RunLineId equals line.Id
                               join run in db.ProductionRuns on line.ProductionRunId equals run.Id
                               where requirement.LibraryPartId != null && partIds.Contains(requirement.LibraryPartId.Value)
                                   && line.Quantity == null && (run.Status == ProductionRunStatus.Active || run.Status == ProductionRunStatus.Complete)
                               select new { PartId = requirement.LibraryPartId!.Value, run.Id, run.Name })
            .Distinct().ToListAsync(cancellationToken);
        var legacyQuery = from job in db.PrintJobs.AsNoTracking()
                          join requirement in db.PartRequirements on job.PartRequirementId equals requirement.Id
                          join line in db.RunLines on requirement.RunLineId equals line.Id
                          join run in db.ProductionRuns on line.ProductionRunId equals run.Id
                          where requirement.LibraryPartId != null && partIds.Contains(requirement.LibraryPartId.Value) && job.ProductionSnapshotJson == null
                          select new { PartId = requirement.LibraryPartId!.Value, job.Id, job.Status, RunId = run.Id, run.Name };
        var legacy = await legacyQuery.Where(j => j.Status != PrintJobStatus.Cancelled && j.Status != PrintJobStatus.Failed && j.Status != PrintJobStatus.Completed)
            .ToListAsync(cancellationToken);
        var terminalCounts = await legacyQuery.Where(j => j.Status == PrintJobStatus.Cancelled || j.Status == PrintJobStatus.Failed || j.Status == PrintJobStatus.Completed)
            .GroupBy(j => j.PartId).Select(g => new { PartId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PartId, g => g.Count, cancellationToken);
        return partIds.Select(id =>
        {
            if (!parts.TryGetValue(id, out var part)) return new PartDeletionEligibility(id, null, "Absent", [], 0);
            var blockers = assemblies.Where(a => a.PartId == id)
                .Select(a => new PartDeletionBlocker("AssemblyReference", "Assembly", a.Id, a.Name))
                .Concat(unlimited.Where(r => r.PartId == id)
                    .Select(r => new PartDeletionBlocker("UnlimitedRun", "ProductionRun", r.Id, r.Name ?? r.Id.ToString())))
                .Concat(legacy.Where(j => j.PartId == id)
                    .Select(j => new PartDeletionBlocker("UnfinishedLegacyJob", "PrintJob", j.Id, $"{j.Name ?? j.RunId.ToString()} — unfinished unresolved legacy job")))
                .OrderBy(b => b.Code).ThenBy(b => b.Name).ThenBy(b => b.EntityId).ToList();
            return new PartDeletionEligibility(id, part.Name, blockers.Count == 0 ? "Eligible" : "Blocked", blockers,
                terminalCounts.GetValueOrDefault(id));
        }).ToList();
    }
}
