using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Maintenance;

namespace OpenFarm.Application.Maintenance;

/// <summary>How newly enrolled printers start their cycle: last done (default now) plus usage from before tracking.</summary>
public sealed record MaintenanceBaseline(DateTimeOffset? LastDoneAtUtc = null, decimal CarryHours = 0, int CarryPrints = 0);

public sealed record MaintenanceDefinition(
    string Name, string? Notes, decimal? EveryPrintHours, int? EveryDays, int? EveryPrints, int WarnAtPercent = MaintenanceTask.DefaultWarnAtPercent);

/// <summary>Defines a task and signs <paramref name="PrinterIds"/> up in one step. Returns the task id.</summary>
public sealed record CreateMaintenanceTaskCommand(MaintenanceDefinition Definition, IReadOnlyList<Guid> PrinterIds, MaintenanceBaseline Baseline);

public sealed record UpdateMaintenanceTaskCommand(Guid TaskId, MaintenanceDefinition Definition);

public sealed record DeleteMaintenanceTaskCommand(Guid TaskId);

/// <summary>Printers already enrolled are skipped. Returns the newly enrolled printer ids.</summary>
public sealed record EnrollPrintersCommand(Guid TaskId, IReadOnlyList<Guid> PrinterIds, MaintenanceBaseline Baseline);

public sealed record UnenrollPrinterCommand(Guid TaskId, Guid PrinterId);

/// <summary>Enrolls one printer in existing tasks from now; tasks it is already in are skipped. Returns the newly enrolled task ids.</summary>
public sealed record EnrollPrinterInTasksCommand(Guid PrinterId, IReadOnlyList<Guid> TaskIds);

/// <summary>"Set last done". Returns the enrollment's printer id.</summary>
public sealed record RebaselineMaintenanceCommand(Guid EnrollmentId, MaintenanceBaseline Baseline);

/// <summary>All or none. Returns the printer id of each completed enrollment.</summary>
public sealed record CompleteMaintenanceCommand(
    IReadOnlyList<Guid> EnrollmentIds, DateTimeOffset? PerformedAtUtc, Guid PerformedByUserId, string? PerformedByNote, string? Notes, decimal? Cost);

public sealed class MaintenanceCommandHandlers(IMaintenanceRepository repository, IMaintenanceUsageReadModel usage) :
    ICommandHandler<CreateMaintenanceTaskCommand, Guid>,
    ICommandHandler<UpdateMaintenanceTaskCommand, Guid>,
    ICommandHandler<DeleteMaintenanceTaskCommand, bool>,
    ICommandHandler<EnrollPrintersCommand, IReadOnlyList<Guid>>,
    ICommandHandler<UnenrollPrinterCommand, bool>,
    ICommandHandler<EnrollPrinterInTasksCommand, IReadOnlyList<Guid>>,
    ICommandHandler<RebaselineMaintenanceCommand, Guid>,
    ICommandHandler<CompleteMaintenanceCommand, IReadOnlyList<Guid>>
{
    public async Task<Guid> HandleAsync(CreateMaintenanceTaskCommand command, CancellationToken cancellationToken)
    {
        var d = command.Definition;
        await EnsureNameFreeAsync(d.Name, null, cancellationToken);
        var task = MaintenanceTask.Create(d.Name, d.Notes, d.EveryPrintHours, d.EveryDays, d.EveryPrints, d.WarnAtPercent);
        repository.Add(task);
        await EnrollAsync(task.Id, command.PrinterIds, [], command.Baseline, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return task.Id;
    }

    public async Task<Guid> HandleAsync(UpdateMaintenanceTaskCommand command, CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(command.TaskId, cancellationToken);
        var d = command.Definition;
        await EnsureNameFreeAsync(d.Name, task.Id, cancellationToken);
        task.Edit(d.Name, d.Notes, d.EveryPrintHours, d.EveryDays, d.EveryPrints, d.WarnAtPercent);
        await repository.SaveChangesAsync(cancellationToken);
        return task.Id;
    }

    public async Task<bool> HandleAsync(DeleteMaintenanceTaskCommand command, CancellationToken cancellationToken)
    {
        repository.Remove(await GetTaskAsync(command.TaskId, cancellationToken));
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> HandleAsync(EnrollPrintersCommand command, CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(command.TaskId, cancellationToken);
        var enrolled = (await repository.ListEnrollmentsAsync(null, task.Id, null, cancellationToken)).Select(r => r.Enrollment.PrinterId);
        var added = await EnrollAsync(task.Id, command.PrinterIds, enrolled.ToHashSet(), command.Baseline, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return added.Select(e => e.PrinterId).ToList();
    }

    public async Task<bool> HandleAsync(UnenrollPrinterCommand command, CancellationToken cancellationToken)
    {
        var enrollment = await repository.GetEnrollmentAsync(command.TaskId, command.PrinterId, cancellationToken)
            ?? throw new MaintenanceNotFoundException("Enrollment");
        repository.Remove(enrollment);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> HandleAsync(EnrollPrinterInTasksCommand command, CancellationToken cancellationToken)
    {
        if ((await repository.ExistingPrinterIdsAsync([command.PrinterId], cancellationToken)).Count == 0)
            throw new PrinterNotFoundException(command.PrinterId);
        var known = (await repository.ListTasksAsync(cancellationToken)).Select(t => t.Id).ToHashSet();
        var wanted = command.TaskIds.Distinct().ToList();
        if (wanted.Any(id => !known.Contains(id))) throw new MaintenanceNotFoundException("Maintenance task");

        var enrolled = (await repository.ListEnrollmentsAsync(command.PrinterId, null, null, cancellationToken)).Select(r => r.Task.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var added = wanted.Where(enrolled.Add).ToList();
        foreach (var taskId in added) repository.Add(MaintenanceEnrollment.Create(taskId, command.PrinterId, now, 0, 0, now));
        await repository.SaveChangesAsync(cancellationToken);
        return added;
    }

    public async Task<Guid> HandleAsync(RebaselineMaintenanceCommand command, CancellationToken cancellationToken)
    {
        var record = (await repository.ListEnrollmentsAsync(null, null, [command.EnrollmentId], cancellationToken)).SingleOrDefault()
            ?? throw new MaintenanceNotFoundException("Enrollment");
        var b = command.Baseline;
        var now = DateTimeOffset.UtcNow;
        record.Enrollment.Rebaseline(b.LastDoneAtUtc ?? now, b.CarryHours, b.CarryPrints, now);
        await repository.SaveChangesAsync(cancellationToken);
        return record.Enrollment.PrinterId;
    }

    public async Task<IReadOnlyList<Guid>> HandleAsync(CompleteMaintenanceCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var performedAt = command.PerformedAtUtc?.ToUniversalTime() ?? now; // timestamptz takes offset 0 only
        var ids = command.EnrollmentIds.Distinct().ToList();
        var records = (await repository.ListEnrollmentsAsync(null, null, ids, cancellationToken)).ToDictionary(r => r.Enrollment.Id);
        // Usage up to the completion time, not up to now, so a backdated completion logs what the printer had done by then.
        var measured = await usage.ReadAsync(
            records.Values.Select(r => new MaintenanceUsageWindow(r.Enrollment.Id, r.Enrollment.PrinterId, r.Enrollment.CycleStartedAtUtc, performedAt)).ToList(),
            cancellationToken);

        var failures = new Dictionary<Guid, string>();
        var logs = new List<MaintenanceLog>();
        foreach (var id in ids)
        {
            if (!records.TryGetValue(id, out var r))
            {
                failures[id] = "Enrollment was not found.";
                continue;
            }

            try
            {
                logs.Add(r.Enrollment.Complete(performedAt, now, r.Task.Name, r.PrinterName, measured[id].Usage,
                    command.PerformedByUserId, command.PerformedByNote, command.Notes, command.Cost));
            }
            catch (MaintenanceException ex)
            {
                failures[id] = ex.Message;
            }
        }

        // Nothing is saved on any failure; the tracked enrollments are discarded with the request scope.
        if (failures.Count > 0) throw new MaintenanceCompletionFailedException(failures);
        foreach (var log in logs) repository.Add(log);
        await repository.SaveChangesAsync(cancellationToken);
        return ids.Select(id => records[id].Enrollment.PrinterId).ToList();
    }

    private async Task<MaintenanceTask> GetTaskAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.GetTaskAsync(id, cancellationToken) ?? throw new MaintenanceNotFoundException("Maintenance task");

    private async Task EnsureNameFreeAsync(string name, Guid? taskId, CancellationToken cancellationToken)
    {
        if (await repository.FindTaskByNameAsync(name?.Trim() ?? "", cancellationToken) is { } other && other.Id != taskId)
            throw new MaintenanceException($"A task named \"{other.Name}\" already exists.");
    }

    private async Task<List<MaintenanceEnrollment>> EnrollAsync(
        Guid taskId, IReadOnlyList<Guid> printerIds, HashSet<Guid> enrolled, MaintenanceBaseline baseline, CancellationToken cancellationToken)
    {
        var wanted = printerIds.Distinct().Where(enrolled.Add).ToList();
        var existing = await repository.ExistingPrinterIdsAsync(wanted, cancellationToken);
        foreach (var id in wanted)
            if (!existing.Contains(id)) throw new PrinterNotFoundException(id);

        var now = DateTimeOffset.UtcNow;
        var added = wanted
            .Select(id => MaintenanceEnrollment.Create(taskId, id, baseline.LastDoneAtUtc ?? now, baseline.CarryHours, baseline.CarryPrints, now))
            .ToList();
        foreach (var enrollment in added) repository.Add(enrollment);
        return added;
    }
}
