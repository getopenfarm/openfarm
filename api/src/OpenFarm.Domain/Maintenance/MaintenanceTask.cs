namespace OpenFarm.Domain.Maintenance;

/// <summary>
/// A maintenance definition shared by every printer enrolled in it. Due at whichever interval (print hours, days,
/// prints) comes first; at least one interval is set.
/// </summary>
public sealed class MaintenanceTask
{
    public const int MaxNameLength = 120;
    public const int DefaultWarnAtPercent = 80;

    private MaintenanceTask(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public string? Notes { get; private set; }

    public decimal? EveryPrintHours { get; private set; }

    public int? EveryDays { get; private set; }

    public int? EveryPrints { get; private set; }

    /// <summary>Due soon from this share of the interval (1–99).</summary>
    public int WarnAtPercent { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static MaintenanceTask Create(
        string name, string? notes, decimal? everyPrintHours, int? everyDays, int? everyPrints, int warnAtPercent = DefaultWarnAtPercent)
    {
        var task = new MaintenanceTask(Guid.NewGuid(), name, DateTimeOffset.UtcNow);
        task.Edit(name, notes, everyPrintHours, everyDays, everyPrints, warnAtPercent);
        return task;
    }

    public void Edit(string name, string? notes, decimal? everyPrintHours, int? everyDays, int? everyPrints, int warnAtPercent)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            throw new MaintenanceException($"Name is required and at most {MaxNameLength} characters.");
        }

        if (everyPrintHours is null && everyDays is null && everyPrints is null)
        {
            throw new MaintenanceException("Set at least one interval.");
        }

        if (everyPrintHours <= 0 || everyDays <= 0 || everyPrints <= 0)
        {
            throw new MaintenanceException("Intervals must be greater than 0.");
        }

        if (warnAtPercent is < 1 or > 99)
        {
            throw new MaintenanceException("Warn at must be 1–99 %.");
        }

        Name = trimmed;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        EveryPrintHours = everyPrintHours;
        EveryDays = everyDays;
        EveryPrints = everyPrints;
        WarnAtPercent = warnAtPercent;
    }
}
