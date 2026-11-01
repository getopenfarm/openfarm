using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Maintenance;
using OpenFarm.Domain.Maintenance;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311, CA1862 // SQL LOWER uses the database collation; ToLowerInvariant is not SQL-translatable.

namespace OpenFarm.Infrastructure.Maintenance;

public sealed class MaintenanceRepository(OpenFarmDbContext dbContext) : IMaintenanceRepository
{
    public async Task<IReadOnlyList<MaintenanceTask>> ListTasksAsync(CancellationToken cancellationToken) =>
        await dbContext.MaintenanceTasks.OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public Task<MaintenanceTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MaintenanceTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<MaintenanceTask?> FindTaskByNameAsync(string name, CancellationToken cancellationToken)
    {
        var lowered = name.Trim().ToLower();
        return dbContext.MaintenanceTasks.FirstOrDefaultAsync(t => t.Name.ToLower() == lowered, cancellationToken);
    }

    public async Task<IReadOnlyList<MaintenanceEnrollmentRecord>> ListEnrollmentsAsync(
        Guid? printerId, Guid? taskId, IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        var enrollments = dbContext.MaintenanceEnrollments.AsQueryable();
        if (printerId is not null) enrollments = enrollments.Where(e => e.PrinterId == printerId);
        if (taskId is not null) enrollments = enrollments.Where(e => e.TaskId == taskId);
        if (ids is not null) enrollments = enrollments.Where(e => ids.Contains(e.Id));

        var rows = await (
            from e in enrollments
            join t in dbContext.MaintenanceTasks on e.TaskId equals t.Id
            join p in dbContext.Printers on e.PrinterId equals p.Id
            select new
            {
                Enrollment = e,
                Task = t,
                PrinterName = p.Name,
                LastLogged = dbContext.MaintenanceLogs.Where(l => l.EnrollmentId == e.Id).Max(l => (DateTimeOffset?)l.PerformedAtUtc),
            }).ToListAsync(cancellationToken);
        return rows.Select(r => new MaintenanceEnrollmentRecord(r.Enrollment, r.Task, r.PrinterName, r.LastLogged)).ToList();
    }

    public Task<MaintenanceEnrollment?> GetEnrollmentAsync(Guid taskId, Guid printerId, CancellationToken cancellationToken) =>
        dbContext.MaintenanceEnrollments.FirstOrDefaultAsync(e => e.TaskId == taskId && e.PrinterId == printerId, cancellationToken);

    public async Task<(IReadOnlyList<MaintenanceLogView> Items, int TotalCount)> ListLogsAsync(
        int page, int pageSize, Guid? printerId, Guid? taskId, CancellationToken cancellationToken)
    {
        var logs = dbContext.MaintenanceLogs.AsNoTracking();
        if (printerId is not null) logs = logs.Where(l => l.PrinterId == printerId);
        if (taskId is not null) logs = logs.Where(l => l.TaskId == taskId);

        var total = await logs.CountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(l => l.PerformedAtUtc).ThenBy(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new MaintenanceLogView(
                l.Id, l.EnrollmentId, l.TaskId,
                dbContext.MaintenanceTasks.Where(t => t.Id == l.TaskId).Select(t => t.Name).FirstOrDefault() ?? l.TaskName,
                l.PrinterId,
                dbContext.Printers.Where(p => p.Id == l.PrinterId).Select(p => p.Name).FirstOrDefault() ?? l.PrinterName,
                l.PerformedAtUtc, l.PerformedByUserId,
                dbContext.Users.Where(u => u.Id == l.PerformedByUserId).Select(u => u.DisplayName ?? u.UserName).FirstOrDefault(),
                l.PerformedByNote, l.Notes, l.Cost, l.HoursAtCompletion, l.PrintsAtCompletion))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlySet<Guid>> ExistingPrinterIdsAsync(IReadOnlyCollection<Guid> printerIds, CancellationToken cancellationToken) =>
        await dbContext.Printers.Where(p => printerIds.Contains(p.Id)).Select(p => p.Id).ToHashSetAsync(cancellationToken);

    public void Add(MaintenanceTask task) => dbContext.MaintenanceTasks.Add(task);

    public void Add(MaintenanceEnrollment enrollment) => dbContext.MaintenanceEnrollments.Add(enrollment);

    public void Add(MaintenanceLog log) => dbContext.MaintenanceLogs.Add(log);

    public void Remove(MaintenanceTask task) => dbContext.MaintenanceTasks.Remove(task);

    public void Remove(MaintenanceEnrollment enrollment) => dbContext.MaintenanceEnrollments.Remove(enrollment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
