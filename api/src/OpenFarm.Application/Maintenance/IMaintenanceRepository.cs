using OpenFarm.Domain.Maintenance;

namespace OpenFarm.Application.Maintenance;

/// <summary>An enrollment with its task, printer name and the time of its latest logged completion.</summary>
public sealed record MaintenanceEnrollmentRecord(
    MaintenanceEnrollment Enrollment, MaintenanceTask Task, string PrinterName, DateTimeOffset? LastLoggedAtUtc);

/// <summary>A log row with live names where the task, printer and user still exist, else the snapshots.</summary>
public sealed record MaintenanceLogView(
    Guid Id, Guid? EnrollmentId, Guid? TaskId, string TaskName, Guid? PrinterId, string PrinterName, DateTimeOffset PerformedAtUtc,
    Guid PerformedByUserId, string? PerformedByName, string? PerformedByNote, string? Notes, decimal? Cost,
    decimal HoursAtCompletion, int PrintsAtCompletion);

public interface IMaintenanceRepository
{
    Task<IReadOnlyList<MaintenanceTask>> ListTasksAsync(CancellationToken cancellationToken);

    Task<MaintenanceTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Case-insensitive.</summary>
    Task<MaintenanceTask?> FindTaskByNameAsync(string name, CancellationToken cancellationToken);

    /// <summary>Every filter is optional; <paramref name="ids"/> narrows to those enrollments. Results are tracked.</summary>
    Task<IReadOnlyList<MaintenanceEnrollmentRecord>> ListEnrollmentsAsync(
        Guid? printerId, Guid? taskId, IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken);

    Task<MaintenanceEnrollment?> GetEnrollmentAsync(Guid taskId, Guid printerId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<MaintenanceLogView> Items, int TotalCount)> ListLogsAsync(
        int page, int pageSize, Guid? printerId, Guid? taskId, CancellationToken cancellationToken);

    /// <summary>Which of <paramref name="printerIds"/> exist.</summary>
    Task<IReadOnlySet<Guid>> ExistingPrinterIdsAsync(IReadOnlyCollection<Guid> printerIds, CancellationToken cancellationToken);

    void Add(MaintenanceTask task);

    void Add(MaintenanceEnrollment enrollment);

    void Add(MaintenanceLog log);

    void Remove(MaintenanceTask task);

    void Remove(MaintenanceEnrollment enrollment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>One enrollment's measuring window: its printer from <paramref name="Since"/> (inclusive) to <paramref name="Until"/>.</summary>
public sealed record MaintenanceUsageWindow(Guid EnrollmentId, Guid PrinterId, DateTimeOffset Since, DateTimeOffset Until);

/// <summary>
/// Measured printer use in a window. <see cref="Hours"/> is a lower bound: <see cref="UntrackedHours"/> of it was not
/// observed (OpenFarm not running).
/// </summary>
public sealed record MaintenanceMeasuredUsage(decimal Hours, int Prints, decimal UntrackedHours)
{
    public MaintenanceUsage Usage => new(Hours, Prints);
}

public interface IMaintenanceUsageReadModel
{
    /// <summary>Usage per enrollment id; every window gets an entry.</summary>
    Task<IReadOnlyDictionary<Guid, MaintenanceMeasuredUsage>> ReadAsync(
        IReadOnlyList<MaintenanceUsageWindow> windows, CancellationToken cancellationToken);
}

public sealed class MaintenanceNotFoundException(string what) : Exception($"{what} was not found.");

/// <summary>Bulk Mark done changed nothing because these enrollments failed.</summary>
public sealed class MaintenanceCompletionFailedException(IReadOnlyDictionary<Guid, string> failures)
    : Exception($"Nothing was marked done: {failures.Count} task(s) failed.")
{
    public IReadOnlyDictionary<Guid, string> Failures { get; } = failures;
}
