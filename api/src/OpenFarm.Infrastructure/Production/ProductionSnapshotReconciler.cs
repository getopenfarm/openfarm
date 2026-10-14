using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

/// <summary>Conservative, idempotent upgrade: current catalog data is evidence only if unchanged since queueing.</summary>
public sealed partial class ProductionSnapshotReconciler(OpenFarmDbContext db, IPartFileStorage storage,
    ILogger<ProductionSnapshotReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var recovered = 0;
        var unresolved = 0;
        var requirements = await db.PartRequirements.ToListAsync(ct);
        var lines = await db.RunLines.ToListAsync(ct);
        foreach (var requirement in requirements)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var part = await db.Parts.FromSqlInterpolated(
                $"SELECT *, xmin FROM \"Parts\" WHERE \"Id\" = {requirement.PartId} FOR UPDATE")
                .Include(p => p.FilamentRequirements).AsNoTracking().FirstOrDefaultAsync(ct);
            if (part is not null) requirement.CaptureName(part.Name);
            var line = lines.First(l => l.Id == requirement.RunLineId);
            if (line.TargetName is null)
            {
                var name = line.TargetKind == OpenFarm.Domain.Production.RunLineTargetKind.Part ? part?.Name
                    : (await db.Assemblies.FirstOrDefaultAsync(a => a.Id == line.TargetId, ct))?.Name;
                if (name is not null) line.CaptureTargetName(name);
            }
            var jobs = await db.PrintJobs.Include(j => j.FilamentConsumption)
                .Where(j => j.PartRequirementId == requirement.Id && j.ProductionSnapshotJson == null && j.SnapshotUnavailableAtUtc == null).ToListAsync(ct);
            foreach (var job in jobs)
            {
                var reason = "Original part metadata cannot be verified; restore the original inputs or cancel and recreate the run.";
                if (part is not null && part.UpdatedAtUtc <= job.CreatedAtUtc && part.RevisionHash == job.FileRevisionHash
                    && part.RevisionNumber == job.FileRevisionNumber && part.PrintRequirementsStatus == PrintRequirementsStatus.Complete
                    && (!part.RequiresSlicing || part.SlicerEngineOverride is not null && part.SlicerVersionUsed is not null))
                {
                    try
                    {
                        await using var bytes = await storage.OpenReadAsync(part.StoragePath, ct);
                        var hash = await PartFileHasher.ComputeHashAsync(bytes, ct);
                        if (hash == job.FileRevisionHash)
                        {
                            job.CaptureProductionSnapshot(part);
                            recovered++;
                            LogRecovered(logger, job.Id);
                            continue;
                        }
                        reason = "The retained file does not match the original job hash.";
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { reason = "The original retained file is missing or unreadable."; }
                }
                unresolved++;
                job.MarkSnapshotUnresolved(reason);
                LogUnresolved(logger, job.Id, reason);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        LogSummary(logger, recovered, unresolved);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Production snapshot reconciliation: {Recovered} recovered, {Unresolved} unresolved")]
    private static partial void LogSummary(ILogger logger, int recovered, int unresolved);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered production snapshot for job {JobId}")]
    private static partial void LogRecovered(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Production snapshot unresolved for job {JobId}: {Reason}")]
    private static partial void LogUnresolved(ILogger logger, Guid jobId, string reason);
}
