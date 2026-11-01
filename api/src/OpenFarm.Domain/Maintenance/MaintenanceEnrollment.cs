namespace OpenFarm.Domain.Maintenance;

/// <summary>
/// One printer signed up to one <see cref="MaintenanceTask"/>. The cycle runs from <see cref="CycleStartedAtUtc"/>;
/// the carries are usage from before tracking began (or before the cycle start), added on top of measured usage.
/// </summary>
public sealed class MaintenanceEnrollment
{
    private MaintenanceEnrollment(Guid id, Guid taskId, Guid printerId)
    {
        Id = id;
        TaskId = taskId;
        PrinterId = printerId;
    }

    public Guid Id { get; }

    public Guid TaskId { get; }

    public Guid PrinterId { get; }

    public DateTimeOffset CycleStartedAtUtc { get; private set; }

    public decimal CarryHours { get; private set; }

    public int CarryPrints { get; private set; }

    public static MaintenanceEnrollment Create(
        Guid taskId, Guid printerId, DateTimeOffset cycleStartedAtUtc, decimal carryHours, int carryPrints, DateTimeOffset now)
    {
        var enrollment = new MaintenanceEnrollment(Guid.NewGuid(), taskId, printerId);
        enrollment.Rebaseline(cycleStartedAtUtc, carryHours, carryPrints, now);
        return enrollment;
    }

    /// <summary>"Set last done": restarts the cycle at <paramref name="lastDoneAtUtc"/> with the given carries.</summary>
    public void Rebaseline(DateTimeOffset lastDoneAtUtc, decimal carryHours, int carryPrints, DateTimeOffset now)
    {
        if (lastDoneAtUtc > now)
        {
            throw new MaintenanceException("Last done cannot be in the future.");
        }

        if (carryHours < 0 || carryPrints < 0)
        {
            throw new MaintenanceException("Usage before tracking cannot be negative.");
        }

        CycleStartedAtUtc = lastDoneAtUtc.ToUniversalTime(); // timestamptz takes offset 0 only
        CarryHours = carryHours;
        CarryPrints = carryPrints;
    }

    /// <summary>
    /// Marks the task done at <paramref name="performedAtUtc"/>, restarting the cycle there. <paramref name="usage"/> is
    /// what the printer did from the cycle start up to <paramref name="performedAtUtc"/>; the log keeps it plus the carries.
    /// </summary>
    public MaintenanceLog Complete(
        DateTimeOffset performedAtUtc,
        DateTimeOffset now,
        string taskName,
        string printerName,
        MaintenanceUsage usage,
        Guid performedByUserId,
        string? performedByNote,
        string? notes,
        decimal? cost)
    {
        if (performedAtUtc > now)
        {
            throw new MaintenanceException("Performed at cannot be in the future.");
        }

        if (performedAtUtc < CycleStartedAtUtc)
        {
            throw new MaintenanceException("Performed at cannot be before the last done date.");
        }

        if (cost < 0)
        {
            throw new MaintenanceException("Cost cannot be negative.");
        }

        var log = new MaintenanceLog(
            Guid.NewGuid(), Id, TaskId, PrinterId, taskName, printerName, performedAtUtc, performedByUserId,
            Clean(performedByNote), Clean(notes), cost, usage.Hours + CarryHours, usage.Prints + CarryPrints);

        CycleStartedAtUtc = performedAtUtc;
        CarryHours = 0;
        CarryPrints = 0;
        return log;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
