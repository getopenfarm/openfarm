using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Maintenance;
using OpenFarm.Application.Common;
using OpenFarm.Application.Maintenance;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="Tasks">Task definitions with their intervals and enrolled printer ids; a task with no printers is not tracked anywhere yet.</param>
/// <param name="Enrollments">Each printer's tasks with usage since last done and due state, most due first.</param>
public sealed record McpMaintenance(IReadOnlyList<MaintenanceTaskResponse> Tasks, IReadOnlyList<MaintenanceEnrollmentResponse> Enrollments);

/// <param name="Enrolled">Printers newly assigned the task.</param>
/// <param name="Rebaselined">Printers already assigned whose last done time was set.</param>
public sealed record McpAssigned(Guid TaskId, string Task, IReadOnlyList<string> Enrolled, IReadOnlyList<string> Rebaselined);

public static class MaintenanceTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_maintenance", "List maintenance",
            "Maintenance tasks, and each printer's assigned tasks with usage since last done and due state (Ok, DueSoon, Overdue), most due first. "
            + "Enrollment ids feed complete_maintenance; task ids feed save_maintenance_task and assign_maintenance.",
            async (RequestContext<CallToolRequestParams> request,
                [Description("Only this printer's tasks")] Guid? printerId = null,
                [Description("Only this task")] Guid? taskId = null,
                [Description("attention (due soon or overdue), Ok, DueSoon or Overdue")] string? state = null,
                CancellationToken ct = default) =>
            {
                var tasks = await request.Service<IQueryHandler<ListMaintenanceTasksQuery, IReadOnlyList<MaintenanceTaskView>>>()
                    .HandleAsync(new ListMaintenanceTasksQuery(taskId), ct);
                var enrollments = await request.Service<IQueryHandler<ListMaintenanceEnrollmentsQuery, IReadOnlyList<MaintenanceEnrollmentView>>>()
                    .HandleAsync(new ListMaintenanceEnrollmentsQuery(printerId, taskId, state), ct);
                return new McpMaintenance(tasks.Select(MaintenanceTaskResponse.From).ToList(), MaintenanceEnrollmentsResponse.From(enrollments).Items);
            },
            "maintenance:view"),
        McpTools.Write("save_maintenance_task", "Save maintenance task",
            "Creates a maintenance task when taskId is omitted, or changes a task for every printer it is assigned to. "
            + "It falls due at whichever interval comes first; set at least one. On a change, pass every interval to keep (an omitted interval is "
            + "removed); omitted notes and warnAtPercent keep their saved values. Assign printers with assign_maintenance.",
            destructive: false, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Task name, such as Lubricate rods")] string name,
                [Description("Due every this many print hours")] decimal? everyPrintHours = null,
                [Description("Due every this many days")] int? everyDays = null,
                [Description("Due every this many prints")] int? everyPrints = null,
                [Description("Percent of the interval at which it shows as due soon, 1–99, default 80")] int? warnAtPercent = null,
                [Description("How to do it, or parts needed")] string? notes = null,
                [Description("Task to change; omit to create one")] Guid? taskId = null,
                CancellationToken ct = default) =>
            {
                var tasks = request.Service<IQueryHandler<ListMaintenanceTasksQuery, IReadOnlyList<MaintenanceTaskView>>>();
                var saved = taskId is { } id
                    ? (await tasks.HandleAsync(new ListMaintenanceTasksQuery(id), ct)).SingleOrDefault()?.Task
                        ?? throw new KeyNotFoundException("Maintenance task not found. Use list_maintenance to find task ids.")
                    : null;
                var dto = new SaveMaintenanceTaskRequest
                {
                    Id = taskId ?? Guid.Empty, Name = name, Notes = notes ?? saved?.Notes, EveryPrintHours = everyPrintHours, EveryDays = everyDays, EveryPrints = everyPrints,
                };
                if ((warnAtPercent ?? saved?.WarnAtPercent) is { } warn) dto.WarnAtPercent = warn;
                var definition = new MaintenanceDefinition(dto.Name, dto.Notes, dto.EveryPrintHours, dto.EveryDays, dto.EveryPrints, dto.WarnAtPercent);
                var commands = request.Service<MaintenanceCommandHandlers>();
                return await request.WriteAsync(dto, async () =>
                {
                    var id = taskId is { } existing
                        ? await commands.HandleAsync(new UpdateMaintenanceTaskCommand(existing, definition), ct)
                        : await commands.HandleAsync(new CreateMaintenanceTaskCommand(definition, [], new MaintenanceBaseline()), ct);
                    return MaintenanceTaskResponse.From((await tasks.HandleAsync(new ListMaintenanceTasksQuery(id), ct)).Single());
                }, ct);
            },
            "maintenance:manage"),
        McpTools.Write("assign_maintenance", "Assign maintenance",
            "Assigns a maintenance task to printers, tracking usage from lastDoneAtUtc (default now). For printers that already have the task, "
            + "lastDoneAtUtc sets when it was last done instead; without it they are left as they are.",
            destructive: false, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Task id from list_maintenance")] Guid taskId,
                [Description("Printer ids from list_printers")] Guid[] printerIds,
                [Description("When the task was last done on these printers (UTC); default now")] DateTimeOffset? lastDoneAtUtc = null,
                CancellationToken ct = default) =>
            {
                if (printerIds is not { Length: > 0 }) throw new ArgumentException("Pass at least one printer id.");
                var commands = request.Service<MaintenanceCommandHandlers>();
                var enrollments = request.Service<IQueryHandler<ListMaintenanceEnrollmentsQuery, IReadOnlyList<MaintenanceEnrollmentView>>>();
                var task = (await request.Service<IQueryHandler<ListMaintenanceTasksQuery, IReadOnlyList<MaintenanceTaskView>>>()
                    .HandleAsync(new ListMaintenanceTasksQuery(taskId), ct)).SingleOrDefault()
                    ?? throw new KeyNotFoundException("Maintenance task not found. Use list_maintenance to find task ids.");
                var existing = (await enrollments.HandleAsync(new ListMaintenanceEnrollmentsQuery(null, taskId, null), ct))
                    .Where(e => printerIds.Contains(e.Record.Enrollment.PrinterId)).ToList();
                var fresh = printerIds.Except(existing.Select(e => e.Record.Enrollment.PrinterId)).Distinct().ToList();
                if (fresh.Count > 0)
                {
                    var dto = new EnrollPrintersRequest { PrinterIds = fresh, LastDoneAtUtc = lastDoneAtUtc };
                    await request.WriteAsync(dto, async () => MaintenanceChangeResponse.ForPrinters(await commands.HandleAsync(
                        new EnrollPrintersCommand(taskId, dto.PrinterIds, new MaintenanceBaseline(lastDoneAtUtc)), ct)), ct);
                }
                if (lastDoneAtUtc is { } done)
                    foreach (var enrollment in existing)
                        await request.WriteAsync(new RebaselineMaintenanceRequest { LastDoneAtUtc = done }, async () => MaintenanceChangeResponse.ForPrinters(
                            [await commands.HandleAsync(new RebaselineMaintenanceCommand(enrollment.Record.Enrollment.Id, new MaintenanceBaseline(done)), ct)]), ct);
                var names = await QueueTools.PrinterNamesAsync(request, ct);
                return new McpAssigned(taskId, task.Task.Name, fresh.Select(id => names.GetValueOrDefault(id, id.ToString())).ToList(),
                    lastDoneAtUtc is null ? [] : existing.Select(e => e.Record.PrinterName).ToList());
            },
            "maintenance:manage"),
        McpTools.Write("complete_maintenance", "Mark maintenance done",
            "Marks maintenance enrollments done, all or none, and starts their next cycle. A failure lists why each enrollment failed.",
            destructive: false, idempotent: false, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Enrollment ids from list_maintenance")] Guid[] enrollmentIds,
                [Description("When the work was done (UTC); default now, not before the cycle start")] DateTimeOffset? performedAtUtc = null,
                [Description("Notes")] string? notes = null,
                [Description("Who did the work, on top of the signed-in user")] string? performedByNote = null,
                [Description("Cost in the farm currency, 0 or more")] decimal? cost = null,
                CancellationToken ct = default) =>
            {
                var dto = new CompleteMaintenanceRequest
                {
                    EnrollmentIds = [.. enrollmentIds ?? []],
                    PerformedAtUtc = performedAtUtc,
                    Notes = notes,
                    PerformedByNote = performedByNote,
                    Cost = cost,
                };
                return await request.WriteAsync(dto, async () => MaintenanceChangeResponse.ForPrinters(
                    await request.Service<MaintenanceCommandHandlers>().HandleAsync(new CompleteMaintenanceCommand(
                        dto.EnrollmentIds, performedAtUtc, request.UserId(), performedByNote, notes, cost), ct)), ct);
            },
            "maintenance:complete"),
    ];
}
