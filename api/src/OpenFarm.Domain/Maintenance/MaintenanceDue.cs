namespace OpenFarm.Domain.Maintenance;

public enum MaintenanceDueState
{
    Ok,
    DueSoon,
    Overdue,
}

public enum MaintenanceTrigger
{
    Hours,
    Days,
    Prints,
}

/// <summary>Measured usage since an enrollment's cycle start, carries not included.</summary>
public readonly record struct MaintenanceUsage(decimal Hours, int Prints);

/// <summary>
/// The trigger closest to (or furthest past) its interval. <see cref="Used"/> includes carries;
/// <see cref="Remaining"/> is <c>Interval − Used</c>, negative when overdue. Units follow <see cref="DrivingTrigger"/>.
/// </summary>
public sealed record MaintenanceDueResult(
    MaintenanceDueState State, MaintenanceTrigger DrivingTrigger, decimal Used, decimal Interval, decimal Remaining);

public static class MaintenanceDue
{
    /// <summary>Due at whichever trigger comes first: the highest <c>(used + carry) / interval</c> drives the result.</summary>
    public static MaintenanceDueResult Evaluate(MaintenanceTask task, MaintenanceEnrollment enrollment, MaintenanceUsage usage, DateTimeOffset now)
    {
        // Elapsed time, not calendar days, so DST and time zones don't shift the due date.
        var elapsedDays = (decimal)(now - enrollment.CycleStartedAtUtc).Ticks / TimeSpan.TicksPerDay;
        (MaintenanceTrigger Trigger, decimal Used, decimal? Interval)[] triggers =
        [
            (MaintenanceTrigger.Hours, usage.Hours + enrollment.CarryHours, task.EveryPrintHours),
            (MaintenanceTrigger.Days, elapsedDays, task.EveryDays),
            (MaintenanceTrigger.Prints, usage.Prints + enrollment.CarryPrints, task.EveryPrints),
        ];

        var (trigger, used, interval) = triggers
            .Where(t => t.Interval is not null)
            .Select(t => (t.Trigger, t.Used, Interval: t.Interval!.Value))
            .MaxBy(t => t.Used / t.Interval);

        var fraction = used / interval;
        var state = fraction >= 1 ? MaintenanceDueState.Overdue
            : fraction * 100 >= task.WarnAtPercent ? MaintenanceDueState.DueSoon
            : MaintenanceDueState.Ok;
        return new MaintenanceDueResult(state, trigger, used, interval, interval - used);
    }
}
