namespace OpenFarm.Domain.Maintenance;

/// <summary>
/// Append-only record of one completion. The ids go null when the enrollment, task or printer is removed; the name
/// snapshots keep the row readable.
/// </summary>
public sealed class MaintenanceLog
{
    internal MaintenanceLog(
        Guid id,
        Guid? enrollmentId,
        Guid? taskId,
        Guid? printerId,
        string taskName,
        string printerName,
        DateTimeOffset performedAtUtc,
        Guid performedByUserId,
        string? performedByNote,
        string? notes,
        decimal? cost,
        decimal hoursAtCompletion,
        int printsAtCompletion)
    {
        Id = id;
        EnrollmentId = enrollmentId;
        TaskId = taskId;
        PrinterId = printerId;
        TaskName = taskName;
        PrinterName = printerName;
        PerformedAtUtc = performedAtUtc;
        PerformedByUserId = performedByUserId;
        PerformedByNote = performedByNote;
        Notes = notes;
        Cost = cost;
        HoursAtCompletion = hoursAtCompletion;
        PrintsAtCompletion = printsAtCompletion;
    }

    public Guid Id { get; }

    public Guid? EnrollmentId { get; }

    public Guid? TaskId { get; }

    public Guid? PrinterId { get; }

    public string TaskName { get; }

    public string PrinterName { get; }

    public DateTimeOffset PerformedAtUtc { get; }

    public Guid PerformedByUserId { get; }

    /// <summary>Free text on top of the signed-in user, e.g. who actually did the work.</summary>
    public string? PerformedByNote { get; }

    public string? Notes { get; }

    public decimal? Cost { get; }

    /// <summary>Print hours in the cycle up to <see cref="PerformedAtUtc"/>, carry included.</summary>
    public decimal HoursAtCompletion { get; }

    /// <summary>Prints in the cycle up to <see cref="PerformedAtUtc"/>, carry included.</summary>
    public int PrintsAtCompletion { get; }
}
