using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Production;

/// <summary>Where a print came from: queued and dispatched by OpenFarm, or started on the printer itself.</summary>
public enum PrintSource
{
    Dispatched,
    External
}

/// <summary>
/// One print on one printer — the shared history row under <see cref="PrintJob"/> (queued/dispatched by OpenFarm) and
/// <see cref="ExternalPrint"/> (started on the printer). Holds the lifecycle and the filament lines, so both kinds deduct
/// filament through the same automatic-deduction path. The transitions are called from one place per print:
/// <c>IJobTelemetrySync</c> for live-integrated printers, the Application command handlers for manual-brand ones.
/// </summary>
public abstract class Print
{
    private readonly List<PrintFilament> _filaments = [];

    protected Print(Guid id, string name, PrintSource source, PrintJobStatus status, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Source = source;
        Status = status;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    /// <summary>The printer it was dispatched to, or that reported it; null for a queued job, and once that printer is deleted.</summary>
    public Guid? PrinterId { get; protected set; }

    public string Name { get; }

    public PrintSource Source { get; }

    public PrintJobStatus Status { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; protected set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>FR-015, derived from Started/EndedAtUtc on close.</summary>
    public int? ActualDurationMinutes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The layer, layer count and percent the printer last reported when the print closed; null when it reported none.</summary>
    public int? StopLayer { get; private set; }

    public int? StopTotalLayers { get; private set; }

    public int? StopPercent { get; private set; }

    /// <summary>The printer's alerts when the print closed (HMS codes, print errors): what stats count as failure codes.</summary>
    public IReadOnlyList<PrinterAlert> StopAlerts { get; private set; } = [];

    /// <summary>Set when the operator chose to leave the print's undeducted filament as is.</summary>
    public DateTimeOffset? FilamentFollowUpDismissedAtUtc { get; private set; }

    public IReadOnlyList<PrintFilament> Filaments => _filaments;

    /// <summary>A line was left undeducted and the operator has not dismissed it.</summary>
    public bool NeedsFilamentFollowUp =>
        FilamentFollowUpDismissedAtUtc is null && _filaments.Any(f => f.SkippedReason is not null);

    public bool IsClosed => Status is PrintJobStatus.Completed or PrintJobStatus.Failed or PrintJobStatus.Cancelled;

    public void MarkStarted()
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Printing);
        Status = PrintJobStatus.Printing;
        StartedAtUtc ??= DateTimeOffset.UtcNow;
    }

    public void MarkPaused()
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Paused);
        Status = PrintJobStatus.Paused;
    }

    public void MarkResumed()
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Printing);
        Status = PrintJobStatus.Printing;
    }

    /// <param name="endedAtUtc">When the print really ended, if OpenFarm saw it only later (<see cref="PrintEnd.Infer"/>); now otherwise.</param>
    public void MarkFailed(string? reason = null, DateTimeOffset? endedAtUtc = null)
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Failed);
        Close(PrintJobStatus.Failed, endedAtUtc);
    }

    public void MarkCompleted(DateTimeOffset? endedAtUtc = null)
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Completed);
        Close(PrintJobStatus.Completed, endedAtUtc);
    }

    /// <summary>See FR-024c for how this is chosen over MarkFailed.</summary>
    public void MarkCancelled(DateTimeOffset? endedAtUtc = null)
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Cancelled);
        Close(PrintJobStatus.Cancelled, endedAtUtc);
    }

    /// <summary>
    /// Freezes which spool each line will draw from, the slicer's full-print grams for it and how they spread over the layers
    /// (aligned with <see cref="Filaments"/> in tool order). Consumption is derived from these on the terminal transition.
    /// </summary>
    public void BindFilamentUsage(IReadOnlyList<(Guid? SpoolId, decimal? EstimatedGrams, FilamentUsageCurve? Curve)> usage)
    {
        if (usage.Count != _filaments.Count) throw new FilamentConsumptionSumMismatchException(null, null);
        var ordered = _filaments.OrderBy(f => f.ToolIndex).ToList();
        for (var i = 0; i < usage.Count; i++) ordered[i].BindUsage(usage[i].SpoolId, usage[i].EstimatedGrams, usage[i].Curve);
    }

    /// <summary>FR-013/FR-013a, callable on any terminal transition.</summary>
    public void RecordFilamentConsumption(Guid lineId, Guid? spoolId, decimal gramsConsumed)
    {
        Line(lineId).Record(spoolId, gramsConsumed);
    }

    /// <summary>Records the price a line is costed at. Only the first price sticks, and no price leaves the line unpriced.</summary>
    public void SnapshotFilamentPrice(Guid lineId, ResolvedFilamentPrice? price)
    {
        if (price is { } resolved)
        {
            Line(lineId).SnapshotPrice(resolved);
        }
    }

    public void RecordStopProgress(int? layer, int? totalLayers, int? percent, IReadOnlyList<PrinterAlert>? alerts = null)
    {
        StopLayer = layer;
        StopTotalLayers = totalLayers;
        StopPercent = percent;
        StopAlerts = alerts ?? [];
    }

    /// <summary>Marks a line automatic deduction could not record, so the operator can follow it up.</summary>
    public void MarkFilamentSkipped(Guid lineId, FilamentSkipReason reason) => Line(lineId).Skip(reason);

    /// <summary>Binds the spool the line really drew from, after the print closed; the caller then re-runs deduction.</summary>
    public void BindLineSpool(Guid lineId, Guid spoolId)
    {
        var line = Line(lineId);
        if (!IsClosed) throw new PrintFilamentNotAssignableException(Id, "the print has not ended.");
        if (line.GramsConsumed is not null) throw new PrintFilamentNotAssignableException(Id, "filament was already deducted for this line.");
        line.Bind(spoolId);
    }

    public void DismissFilamentFollowUp() => FilamentFollowUpDismissedAtUtc ??= DateTimeOffset.UtcNow;

    private PrintFilament Line(Guid lineId) =>
        _filaments.FirstOrDefault(l => l.Id == lineId) ?? throw new FilamentConsumptionSumMismatchException(null, null);

    protected void AddFilament(PrintFilament line) => _filaments.Add(line);

    protected void ClearFilaments() => _filaments.Clear();

    /// <summary>Runs once the print reaches a terminal status, after <see cref="EndedAtUtc"/> is set.</summary>
    protected virtual void OnClosed()
    {
    }

    private void Close(PrintJobStatus terminalStatus, DateTimeOffset? endedAtUtc)
    {
        Status = terminalStatus;
        var ended = endedAtUtc ?? DateTimeOffset.UtcNow;
        // An inferred end never comes before the start.
        EndedAtUtc = StartedAtUtc is { } started && ended < started ? started : ended;
        if (StartedAtUtc is not null)
        {
            ActualDurationMinutes = (int)Math.Round((EndedAtUtc.Value - StartedAtUtc.Value).TotalMinutes);
        }

        OnClosed();
    }
}
