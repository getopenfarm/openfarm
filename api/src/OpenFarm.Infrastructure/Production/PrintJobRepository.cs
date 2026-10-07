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
        dbContext.PrintJobs.Include(j => j.FilamentConsumption).FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> ListByPartRequirementIdAsync(Guid partRequirementId, CancellationToken cancellationToken) =>
        await dbContext.PrintJobs.Include(j => j.FilamentConsumption)
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
            .Include(j => j.FilamentConsumption)
            .ToListAsync(cancellationToken);

    /// <summary>The dispatcher's own reported-state lookup (research.md R12) — non-terminal, dispatched to this printer.</summary>
    public Task<PrintJob?> GetLastJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        dbContext.PrintJobs.Where(j => j.DispatchedPrinterId == printerId)
            .OrderByDescending(j => j.EndedAtUtc).FirstOrDefaultAsync(cancellationToken);

    public Task<PrintJob?> GetActiveJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        dbContext.PrintJobs
            .Where(j => j.DispatchedPrinterId == printerId && !TerminalStatuses.Contains(j.Status))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Constitution Durable Recovery guardrail — dispatched but never confirmed Printing by telemetry, from a prior process lifetime.</summary>
    public async Task<IReadOnlyList<PrintJob>> ListDispatchInFlightAsync(CancellationToken cancellationToken) =>
        await dbContext.PrintJobs
            .Where(j => j.DispatchedAtUtc != null && j.Status == PrintJobStatus.Queued)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// research.md R3 — new/requeued jobs are pre-spaced past the current top of the queue.
    /// <paramref name="organizationId"/> is accepted for caller clarity (e.g. a background service
    /// with no ambient HTTP request) but is never used as a manual filter here — tenant scoping is
    /// Finbuckle's global query filter alone (constitution: hand-rolled per-query tenant `WHERE`
    /// clauses are prohibited), which already restricts <c>dbContext.PrintJobs</c> to whichever
    /// tenant is set on this scoped <see cref="OpenFarmDbContext"/>.
    /// </summary>
    public async Task<double?> GetMaxQueuePositionAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.PrintJobs.AnyAsync(cancellationToken)
            ? await dbContext.PrintJobs.MaxAsync(j => j.QueuePosition, cancellationToken)
            : null;

    public async Task<(IReadOnlyList<PrintJob> Jobs, int TotalCount)> ListQueueAsync(
        bool backfillOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query =
            from job in dbContext.PrintJobs
            join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
            where backfillOnly ? requirement.Quantity == null : requirement.Quantity != null
            where job.Status != PrintJobStatus.Cancelled
            orderby job.QueuePosition
            select job;

        var totalCount = await query.CountAsync(cancellationToken);
        var jobs = await query.Include(j => j.FilamentConsumption).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (jobs, totalCount);
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

    public void TrackNewFilamentConsumption(IReadOnlyList<JobFilamentConsumption> lines) => dbContext.JobFilamentConsumptions.AddRange(lines);

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
