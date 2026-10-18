namespace OpenFarm.Domain.Production;

/// <summary>One filament (tool) of an external print: the slicer's grams and curve, read from the file the printer printed.</summary>
public sealed class ExternalPrintFilament : IFilamentUsageLine
{
    private ExternalPrintFilament(Guid id, Guid organizationId, Guid externalPrintId, int toolIndex, string? materialType,
        string? colorHex, decimal? estimatedGrams, FilamentUsageCurve? usageCurve)
    {
        Id = id;
        OrganizationId = organizationId;
        ExternalPrintId = externalPrintId;
        ToolIndex = toolIndex;
        MaterialType = materialType;
        ColorHex = colorHex;
        EstimatedGrams = estimatedGrams;
        UsageCurve = usageCurve;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid ExternalPrintId { get; }

    public int ToolIndex { get; }

    public string? MaterialType { get; }

    public string? ColorHex { get; }

    public Guid? SpoolId { get; private set; }

    public decimal? EstimatedGrams { get; }

    public decimal? GramsConsumed { get; private set; }

    public FilamentUsageCurve? UsageCurve { get; }

    internal static ExternalPrintFilament Create(Guid organizationId, Guid externalPrintId, ExternalFilamentSpec spec) =>
        new(Guid.NewGuid(), organizationId, externalPrintId, spec.ToolIndex, spec.MaterialType, spec.ColorHex, spec.Grams, spec.Curve);

    internal void Bind(Guid? spoolId) => SpoolId = spoolId;

    internal void Record(Guid? spoolId, decimal grams)
    {
        SpoolId = spoolId;
        GramsConsumed = grams;
    }
}

public sealed record ExternalFilamentSpec(int ToolIndex, string? MaterialType, string? ColorHex, decimal? Grams, FilamentUsageCurve? Curve);

/// <summary>
/// A print the printer started itself (Bambu Studio, the Handy app, the touchscreen, an SD card): no queue, run or part behind it.
/// Exists only so its filament is deducted like a dispatched job's. Which spool fed each tool isn't reported, so the trays the
/// printer names as active are recorded as they appear; tools, in tool order, are matched to those trays in order of appearance.
/// </summary>
public sealed class ExternalPrint
{
    private readonly List<ExternalPrintFilament> _filaments = [];

    private ExternalPrint(Guid id, Guid organizationId, Guid printerId, string jobName, DateTimeOffset startedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        PrinterId = printerId;
        JobName = jobName;
        StartedAtUtc = startedAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid PrinterId { get; }

    public string JobName { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>Null while printing; <see cref="PrintJobStatus.Completed"/> or <see cref="PrintJobStatus.Failed"/> once the printer reports an end.</summary>
    public PrintJobStatus? Outcome { get; private set; }

    /// <summary>Active tray ids in order of first appearance (<c>unit*4+slot</c>, 254 external); paired with <see cref="ObservedSpoolIds"/> (Guid.Empty = no spool known).</summary>
    public int[] ObservedTrayIds { get; private set; } = [];

    public Guid[] ObservedSpoolIds { get; private set; } = [];

    public IReadOnlyList<ExternalPrintFilament> Filaments => _filaments;

    public static ExternalPrint Start(Guid organizationId, Guid printerId, string jobName, IEnumerable<ExternalFilamentSpec> filaments)
    {
        var print = new ExternalPrint(Guid.NewGuid(), organizationId, printerId, jobName, DateTimeOffset.UtcNow);
        foreach (var spec in filaments.OrderBy(f => f.ToolIndex))
            print._filaments.Add(ExternalPrintFilament.Create(organizationId, print.Id, spec));
        return print;
    }

    /// <summary>Records a newly active tray (and the spool loaded there); false when it was already seen.</summary>
    public bool ObserveTray(int trayId, Guid? spoolId)
    {
        if (Outcome is not null || ObservedTrayIds.Contains(trayId)) return false;
        ObservedTrayIds = [.. ObservedTrayIds, trayId];
        ObservedSpoolIds = [.. ObservedSpoolIds, spoolId ?? Guid.Empty];
        return true;
    }

    /// <summary>
    /// Ends the print and binds spools to tools. One tool takes the first tray seen; several tools bind only when exactly as
    /// many trays were seen, otherwise they stay unbound and deduction is skipped rather than guessed.
    /// </summary>
    public void Finish(PrintJobStatus outcome)
    {
        if (Outcome is not null) return;
        Outcome = outcome;
        EndedAtUtc = DateTimeOffset.UtcNow;
        if (_filaments.Count == 1 && ObservedSpoolIds.Length > 0) _filaments[0].Bind(Known(0));
        else if (_filaments.Count > 1 && _filaments.Count == ObservedSpoolIds.Length)
            for (var i = 0; i < _filaments.Count; i++) _filaments[i].Bind(Known(i));
    }

    private Guid? Known(int index) => ObservedSpoolIds[index] == Guid.Empty ? null : ObservedSpoolIds[index];

    public void RecordFilamentConsumption(Guid lineId, Guid? spoolId, decimal grams) =>
        _filaments.First(f => f.Id == lineId).Record(spoolId, grams);
}
