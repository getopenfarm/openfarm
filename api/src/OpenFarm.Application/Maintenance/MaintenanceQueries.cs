using OpenFarm.Application.Common;
using OpenFarm.Domain.Maintenance;

namespace OpenFarm.Application.Maintenance;

/// <summary><paramref name="State"/>: <c>attention</c> (due soon or overdue) or a <see cref="MaintenanceDueState"/> name.</summary>
public sealed record ListMaintenanceEnrollmentsQuery(Guid? PrinterId = null, Guid? TaskId = null, string? State = null);

public sealed record MaintenanceEnrollmentView(
    MaintenanceEnrollmentRecord Record, MaintenanceMeasuredUsage Usage, MaintenanceDueResult Due);

/// <summary>Due state is computed on read from printer activity and prints since each enrollment's cycle start.</summary>
public sealed class ListMaintenanceEnrollmentsQueryHandler(IMaintenanceRepository repository, IMaintenanceUsageReadModel usage)
    : IQueryHandler<ListMaintenanceEnrollmentsQuery, IReadOnlyList<MaintenanceEnrollmentView>>
{
    public async Task<IReadOnlyList<MaintenanceEnrollmentView>> HandleAsync(ListMaintenanceEnrollmentsQuery query, CancellationToken cancellationToken)
    {
        MaintenanceDueState[]? states = query.State switch
        {
            null or "" => null,
            "attention" => [MaintenanceDueState.DueSoon, MaintenanceDueState.Overdue],
            _ when Enum.TryParse<MaintenanceDueState>(query.State, ignoreCase: true, out var state) => [state],
            _ => throw new ArgumentException("Use state=attention, Ok, DueSoon or Overdue."),
        };

        var now = DateTimeOffset.UtcNow;
        var records = await repository.ListEnrollmentsAsync(query.PrinterId, query.TaskId, null, cancellationToken);
        var measured = await usage.ReadAsync(
            records.Select(r => new MaintenanceUsageWindow(r.Enrollment.Id, r.Enrollment.PrinterId, r.Enrollment.CycleStartedAtUtc, now)).ToList(),
            cancellationToken);

        return records
            .Select(r => new MaintenanceEnrollmentView(
                r, measured[r.Enrollment.Id], MaintenanceDue.Evaluate(r.Task, r.Enrollment, measured[r.Enrollment.Id].Usage, now)))
            .Where(v => states is null || states.Contains(v.Due.State))
            .OrderByDescending(v => v.Due.Used / v.Due.Interval)
            .ThenBy(v => v.Record.PrinterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed record ListMaintenanceTasksQuery(Guid? TaskId = null);

/// <summary><see cref="WorstState"/> is null while no printer is enrolled.</summary>
public sealed record MaintenanceTaskView(MaintenanceTask Task, IReadOnlyList<Guid> PrinterIds, MaintenanceDueState? WorstState);

public sealed class ListMaintenanceTasksQueryHandler(
    IMaintenanceRepository repository,
    IQueryHandler<ListMaintenanceEnrollmentsQuery, IReadOnlyList<MaintenanceEnrollmentView>> enrollments)
    : IQueryHandler<ListMaintenanceTasksQuery, IReadOnlyList<MaintenanceTaskView>>
{
    public async Task<IReadOnlyList<MaintenanceTaskView>> HandleAsync(ListMaintenanceTasksQuery query, CancellationToken cancellationToken)
    {
        var tasks = await repository.ListTasksAsync(cancellationToken);
        var byTask = (await enrollments.HandleAsync(new ListMaintenanceEnrollmentsQuery(TaskId: query.TaskId), cancellationToken))
            .ToLookup(v => v.Record.Task.Id);

        return tasks
            .Where(t => query.TaskId is null || t.Id == query.TaskId)
            .Select(t => new MaintenanceTaskView(
                t,
                byTask[t.Id].Select(v => v.Record.Enrollment.PrinterId).ToList(),
                byTask[t.Id].Any() ? byTask[t.Id].Max(v => v.Due.State) : null))
            .ToList();
    }
}

public sealed record ListMaintenanceLogsQuery(int Page, int PageSize, Guid? PrinterId = null, Guid? TaskId = null);

public sealed class ListMaintenanceLogsQueryHandler(IMaintenanceRepository repository)
    : IQueryHandler<ListMaintenanceLogsQuery, (IReadOnlyList<MaintenanceLogView> Items, int TotalCount)>
{
    public Task<(IReadOnlyList<MaintenanceLogView> Items, int TotalCount)> HandleAsync(ListMaintenanceLogsQuery query, CancellationToken cancellationToken) =>
        repository.ListLogsAsync(query.Page, query.PageSize, query.PrinterId, query.TaskId, cancellationToken);
}
