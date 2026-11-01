using OpenFarm.Api.Processors;
using OpenFarm.Application.Maintenance;

namespace OpenFarm.Api.Endpoints.Maintenance;

/// <summary><see cref="WorstState"/> (Ok, DueSoon, Overdue) is null while no printer is enrolled.</summary>
public sealed record MaintenanceTaskResponse(
    Guid Id, string Name, string? Notes, decimal? EveryPrintHours, int? EveryDays, int? EveryPrints, int WarnAtPercent,
    DateTimeOffset CreatedAtUtc, IReadOnlyList<Guid> PrinterIds, string? WorstState)
{
    public static MaintenanceTaskResponse From(MaintenanceTaskView v) => new(
        v.Task.Id, v.Task.Name, v.Task.Notes, v.Task.EveryPrintHours, v.Task.EveryDays, v.Task.EveryPrints, v.Task.WarnAtPercent,
        v.Task.CreatedAtUtc, v.PrinterIds, v.WorstState?.ToString());
}

public sealed record MaintenanceTasksResponse(IReadOnlyList<MaintenanceTaskResponse> Items);

/// <summary>
/// One printer's sign-up to a task. <c>Hours</c>/<c>Prints</c> are measured since <c>CycleStartedAtUtc</c>, carries excluded;
/// hours are a lower bound when <c>UntrackedHours</c> &gt; 0. <c>LastDoneAtUtc</c> is the latest logged completion (null: the
/// cycle start is only a baseline). <c>State</c>, <c>DrivingTrigger</c> (Hours, Days, Prints), <c>Used</c> (carries included),
/// <c>Interval</c> and <c>Remaining</c> (negative when overdue) describe the trigger closest to due, in its own unit.
/// </summary>
public sealed record MaintenanceEnrollmentResponse(
    Guid Id, Guid TaskId, string TaskName, Guid PrinterId, string PrinterName, DateTimeOffset CycleStartedAtUtc, DateTimeOffset? LastDoneAtUtc,
    decimal CarryHours, int CarryPrints, decimal? EveryPrintHours, int? EveryDays, int? EveryPrints, int WarnAtPercent,
    decimal Hours, int Prints, decimal UntrackedHours, string State, string DrivingTrigger, decimal Used, decimal Interval, decimal Remaining)
{
    public static MaintenanceEnrollmentResponse From(MaintenanceEnrollmentView v)
    {
        var (e, t) = (v.Record.Enrollment, v.Record.Task);
        return new(e.Id, t.Id, t.Name, e.PrinterId, v.Record.PrinterName, e.CycleStartedAtUtc, v.Record.LastLoggedAtUtc,
            e.CarryHours, e.CarryPrints, t.EveryPrintHours, t.EveryDays, t.EveryPrints, t.WarnAtPercent,
            v.Usage.Hours, v.Usage.Prints, v.Usage.UntrackedHours, v.Due.State.ToString(), v.Due.DrivingTrigger.ToString(),
            Math.Round(v.Due.Used, 2), v.Due.Interval, Math.Round(v.Due.Remaining, 2));
    }
}

public sealed record MaintenanceEnrollmentsResponse(IReadOnlyList<MaintenanceEnrollmentResponse> Items);

public sealed record MaintenanceLogsResponse(IReadOnlyList<MaintenanceLogView> Items, int TotalCount);

/// <summary>
/// <see cref="EntityIds"/>: the printers whose maintenance changed, one per changed enrollment (logged on each printer's
/// timeline). <see cref="Count"/>: how many enrollments changed.
/// </summary>
public sealed class MaintenanceChangeResponse : IHasEntityIds
{
    public IReadOnlyList<string> EntityIds { get; init; } = [];

    public int Count { get; init; }

    public static MaintenanceChangeResponse ForPrinters(IReadOnlyList<Guid> printerIds) =>
        new() { EntityIds = printerIds.Select(id => id.ToString()).ToList(), Count = printerIds.Count };
}
