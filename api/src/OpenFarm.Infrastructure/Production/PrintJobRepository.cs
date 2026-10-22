using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

public sealed class PrintJobRepository(OpenFarmDbContext dbContext) : IPrintJobRepository
{
    private static readonly PrintJobStatus[] TerminalStatuses =
        [PrintJobStatus.Completed, PrintJobStatus.Failed, PrintJobStatus.Cancelled];

    public Task AddAsync(PrintJob job, CancellationToken cancellationToken)
    {
        dbContext.PrintJobs.Add(job);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken)
    {
        dbContext.PrintJobs.AddRange(jobs);
        return Task.CompletedTask;
    }

    public Task<PrintJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PrintJobs.Include(j => j.Filaments).FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> ListByPartRequirementIdAsync(Guid partRequirementId, CancellationToken cancellationToken) =>
        await dbContext.PrintJobs.Include(j => j.Filaments)
            .Where(j => j.PartRequirementId == partRequirementId)
            .OrderBy(j => j.QueuePosition)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> ListByProductionRunIdAsync(Guid productionRunId, CancellationToken cancellationToken) =>
        await (
            from job in dbContext.PrintJobs
            join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
            join line in dbContext.RunLines on requirement.RunLineId equals line.Id
            where line.ProductionRunId == productionRunId
            orderby job.QueuePosition
            select job)
            .Include(j => j.Filaments)
            .ToListAsync(cancellationToken);

    /// <summary>The dispatcher's own reported-state lookup (research.md R12) — non-terminal, dispatched to this printer.</summary>
    public Task<PrintJob?> GetLastJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        dbContext.PrintJobs.Where(j => j.PrinterId == printerId)
            .OrderByDescending(j => j.EndedAtUtc).FirstOrDefaultAsync(cancellationToken);

    public Task<PrintJob?> GetActiveJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        dbContext.PrintJobs.Include(j => j.Filaments)
            .Where(j => j.PrinterId == printerId && !TerminalStatuses.Contains(j.Status))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Constitution Durable Recovery guardrail — dispatched but never confirmed Printing by telemetry, from a prior process lifetime.</summary>
    public async Task<IReadOnlyList<PrintJob>> ListDispatchInFlightAsync(CancellationToken cancellationToken) =>
        await dbContext.PrintJobs
            .Where(j => j.DispatchedAtUtc != null && j.Status == PrintJobStatus.Queued)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> ListUnfinishedFiniteAsync(CancellationToken cancellationToken) =>
        await dbContext.PrintJobs.AsNoTracking().Include(j => j.Filaments)
            .Where(j => j.Status == PrintJobStatus.Queued || j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused)
            .Where(j => dbContext.PartRequirements.Any(r => r.Id == j.PartRequirementId && r.Quantity != null))
            .ToListAsync(cancellationToken);

    /// <summary>research.md R3 — new/requeued jobs are pre-spaced past the current top of the queue.</summary>
    public async Task<double?> GetMaxQueuePositionAsync(CancellationToken cancellationToken) =>
        await dbContext.PrintJobs.AnyAsync(cancellationToken)
            ? await dbContext.PrintJobs.MaxAsync(j => j.QueuePosition, cancellationToken)
            : null;

    public async Task<(IReadOnlyList<PrintJob> Jobs, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts)> ListQueueAsync(
        bool backfillOnly, int page, int pageSize, CancellationToken cancellationToken, string? status = null, string? search = null)
    {
        var query =
            from job in dbContext.PrintJobs
            join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
            where backfillOnly ? requirement.Quantity == null : requirement.Quantity != null
            select job;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            query = from job in query
                    join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
                    where EF.Functions.ILike(requirement.PartName ?? "", $"%{term}%", "\\")
                    select job;
        }

        var counts = await query.GroupBy(j => j.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var statusCounts = Enum.GetValues<PrintJobStatus>().ToDictionary(value => value.ToString(), _ => 0);
        foreach (var count in counts) statusCounts[count.Status.ToString()] = count.Count;

        if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(j => !TerminalStatuses.Contains(j.Status));
        else if (Enum.TryParse<PrintJobStatus>(status, true, out var selectedStatus))
            query = query.Where(j => j.Status == selectedStatus);
        else
            query = query.Where(j => j.Status != PrintJobStatus.Cancelled);

        var totalCount = await query.CountAsync(cancellationToken);
        var jobs = await query.OrderBy(j => j.QueuePosition).ThenBy(j => j.Id)
            .Include(j => j.Filaments).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (jobs, totalCount, statusCounts);
    }

    public Task<PrintJob?> GetNextQueuedJobAsync(double afterQueuePosition, Guid excludeJobId, CancellationToken cancellationToken) =>
        dbContext.PrintJobs
            .Where(j => j.Status == PrintJobStatus.Queued && j.QueuePosition > afterQueuePosition && j.Id != excludeJobId)
            .OrderBy(j => j.QueuePosition)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<double?> GetMinQueuedPositionAsync(CancellationToken cancellationToken) =>
        await dbContext.PrintJobs.AnyAsync(j => j.Status == PrintJobStatus.Queued, cancellationToken)
            ? await dbContext.PrintJobs.Where(j => j.Status == PrintJobStatus.Queued).MinAsync(j => j.QueuePosition, cancellationToken)
            : null;

    public void TrackNewFilamentConsumption(IReadOnlyList<PrintFilament> lines) => dbContext.PrintFilaments.AddRange(lines);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var jobId = ex.Entries.Select(e => e.Entity).OfType<PrintJob>().FirstOrDefault()?.Id ?? Guid.Empty;
            throw new ProductionRunConcurrencyConflictException(jobId);
        }
    }
}
