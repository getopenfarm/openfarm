using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Printers;

public sealed class Printer
{
    private Printer(
        Guid id,
        string name,
        string brand,
        string modelCode,
        string ipAddress,
        string serialNumber,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Brand = brand;
        ModelCode = modelCode;
        IpAddress = ipAddress;
        SerialNumber = serialNumber;
        Status = PrinterStatus.Idle;
        IsReachable = false;
        FeedReadings = [];
        Extruders = [];
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public string Brand { get; private set; }

    public string ModelCode { get; private set; }

    public string IpAddress { get; private set; }

    public string SerialNumber { get; }

    public PrinterStatus Status { get; private set; }

    public string? ReportedState { get; private set; }
    public DateTimeOffset? StateObservedAtUtc { get; private set; }
    public IReadOnlyList<PrinterAlert> ActiveAlerts { get; private set; } = [];

    public bool ObserveReportedState(string? state, DateTimeOffset atUtc)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        var changed = ReportedState != state;
        ReportedState = state;
        StateObservedAtUtc = atUtc;
        return changed;
    }

    // Null means absent from a delta; an empty list explicitly clears that source. The first list is the printer's
    // device alerts (Bambu HMS, Snapmaker exceptions), so every source except PrintError belongs to it.
    public bool ObserveAlerts(IReadOnlyList<PrinterAlert>? hms, IReadOnlyList<PrinterAlert>? printErrors)
    {
        var merged = (hms ?? ActiveAlerts.Where(a => a.Source != "PrintError").ToArray())
            .Concat(printErrors ?? ActiveAlerts.Where(a => a.Source == "PrintError").ToArray())
            .Distinct().OrderBy(a => a.Source, StringComparer.Ordinal).ThenBy(a => a.Code, StringComparer.Ordinal).ToArray();
        if (ActiveAlerts.SequenceEqual(merged)) return false;
        ActiveAlerts = merged;
        return true;
    }

    public bool IsReachable { get; private set; }

    public DateTimeOffset? LastMessageUtc { get; private set; }

    /// <summary>The nozzle every extruder shares, or null when they differ — for display; production asks per extruder.</summary>
    public decimal? ReportedNozzleDiameterMm { get; private set; }
    public DateTimeOffset? NozzleObservedAtUtc { get; private set; }

    /// <summary>Some extruder's nozzle is confirmed on the current connection.</summary>
    public bool NozzleConfirmed { get; private set; }

    /// <summary>A new connection confirms nothing until the printer reports again; the last diameters are kept for display.</summary>
    public void BeginConnection()
    {
        NozzleConfirmed = false;
        Extruders = WithExtruders(Extruders.Count, (extruder, _) => extruder with { NozzleConfirmed = false });
    }

    /// <summary>The same nozzle on every extruder the printer has reported, at least one.</summary>
    public void ObserveNozzle(decimal? diameterMm, DateTimeOffset observedAtUtc)
    {
        if (diameterMm is not > 0) return;
        ObserveNozzles(Enumerable.Repeat(diameterMm, Math.Max(Extruders.Count, 1)).ToList(), observedAtUtc);
    }

    /// <summary>
    /// Records and confirms each extruder's reported nozzle (index = extruder). An unknown diameter unconfirms only that
    /// extruder, keeping its last diameter.
    /// </summary>
    public void ObserveNozzles(IReadOnlyList<decimal?> diametersMm, DateTimeOffset observedAtUtc)
    {
        if (diametersMm.Count == 0) return;
        Extruders = WithExtruders(diametersMm.Count, (extruder, index) => diametersMm[index] is > 0
            ? extruder with { NozzleDiameterMm = diametersMm[index], NozzleConfirmed = true }
            : extruder with { NozzleConfirmed = false });
        if (diametersMm.Any(d => d is > 0)) NozzleObservedAtUtc = observedAtUtc;
        NozzleConfirmed = Extruders.Any(e => e.NozzleConfirmed);
        ReportedNozzleDiameterMm = Extruders.Select(e => e.NozzleDiameterMm).Distinct().ToList() is [{ } shared] ? shared : null;
    }

    /// <summary>The extruder's nozzle confirmed on the current connection, or null.</summary>
    public decimal? ConfirmedNozzleMm(int extruderIndex) =>
        Extruders.FirstOrDefault(e => e.Index == extruderIndex) is { NozzleConfirmed: true, NozzleDiameterMm: { } diameter } ? diameter : null;

    /// <summary>Each distinct nozzle confirmed on the current connection, smallest first.</summary>
    public IReadOnlyList<decimal> ConfirmedNozzlesMm() =>
        Extruders.Where(e => e.NozzleConfirmed).Select(e => e.NozzleDiameterMm).OfType<decimal>().Distinct().Order().ToList();

    /// <summary>Each distinct nozzle the extruders last reported, confirmed or not, smallest first.</summary>
    public IReadOnlyList<decimal> ReportedNozzlesMm() =>
        Extruders.Select(e => e.NozzleDiameterMm).OfType<decimal>().Distinct().Order().ToList();

    public decimal? NozzleTempC { get; private set; }

    public decimal? BedTempC { get; private set; }

    public int? PrintProgressPercent { get; private set; }

    public int? CurrentLayer { get; private set; }

    public int? TotalLayers { get; private set; }

    public string? CurrentStageLabel { get; private set; }

    public int? RemainingTimeMinutes { get; private set; }

    public string? CurrentJobName { get; private set; }

    /// <summary>The printer's own <c>gcode_file</c> for the current print — where an external print's file can be found.</summary>
    public string? CurrentGcodeFile { get; private set; }

    /// <summary>The feed units and slots the printer last reported, with what it detects in each.</summary>
    public IReadOnlyList<FeedUnitReading> FeedReadings { get; private set; }

    /// <summary>What the printer last reported per extruder (index = extruder): its nozzle and the slot feeding it.</summary>
    public IReadOnlyList<ExtruderReading> Extruders { get; private set; }

    /// <summary>
    /// Optional link to a Machine Profile (Phase 2), filling the deferred link `docs/FEATURE_PLAN.md`'s
    /// own Phase 1 notes earmarked (FR-005b). `null` at creation time — a matching profile might not be
    /// imported yet.
    /// </summary>
    public Guid? MachineProfileId { get; private set; }

    /// <summary>
    /// Optional link to a Process Profile (009), parallel to <see cref="MachineProfileId"/> — one
    /// process selection per printer, not per machine profile, so two printers sharing a machine
    /// profile can independently pick different processes. Gated on machine-selected-first and
    /// machine/process compatibility in the Application-layer command handler, not here (data-model.md).
    /// </summary>
    public Guid? ProcessProfileId { get; private set; }

    /// <summary>
    /// The printer's own currently-installed physical build plate (012) — independent of
    /// <see cref="MachineProfileId"/>/<see cref="ProcessProfileId"/>, no machine-profile prerequisite,
    /// and never re-validated by <see cref="LinkMachineProfile"/>/<see cref="UnlinkMachineProfile"/>
    /// (FR-008). <c>null</c> for every printer until an operator selects one (data-model.md).
    /// </summary>
    public BuildPlateType? BuildPlateType { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Printer Create(
        string name,
        string brand,
        string modelCode,
        string ipAddress,
        string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Printer name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            throw new ArgumentException("Printer IP address is required.", nameof(ipAddress));
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Printer serial number is required.", nameof(serialNumber));
        }

        // Validates (brand, modelCode) resolves against the catalog; throws UnknownPrinterModelException otherwise.
        var model = PrinterModelCatalog.Resolve(brand, modelCode);

        return new Printer(Guid.NewGuid(), name, model.Brand.ToString(), model.ModelCode, ipAddress, serialNumber, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The only manual status transition this phase supports: AwaitingClear -&gt; Idle ("Mark ready").
    /// </summary>
    public void TransitionTo(PrinterStatus target)
    {
        PrinterStatusTransitions.EnsureValidManualTransition(Status, target);
        Status = target;
    }

    /// <summary>
    /// Reconciles to a connector-reported gcode_state, regardless of the strict manual-transition
    /// rules (FR-004). Returns whether the status actually changed, for callers deciding whether to
    /// log/emit an event; never throws.
    /// </summary>
    public bool ObserveStatus(string gcodeState)
    {
        var observed = PrinterStatusTransitions.ObserveStatus(gcodeState);
        if (observed is null || observed == Status || (Status == PrinterStatus.AwaitingClear && observed == PrinterStatus.Idle))
        {
            return false;
        }

        Status = observed.Value;
        return true;
    }

    public void MarkReachable(DateTimeOffset atUtc)
    {
        IsReachable = true;
        LastMessageUtc = atUtc;
    }

    public void MarkUnreachable()
    {
        IsReachable = false;
        BeginConnection();
    }

    /// <summary>
    /// Printer reports are deltas (Bambu's MQTT, research.md §1): a missing field means "unchanged", not "cleared". Each
    /// nullable parameter therefore falls back to the previously recorded value rather than overwriting it with null.
    /// <paramref name="feedReadings"/> replaces only the unit kinds it reports — an AMS delta and an external-spool delta
    /// arrive separately — so an empty list is "unchanged". <paramref name="activeSlots"/> is per extruder (index = extruder;
    /// a null entry means nothing feeds it), or null when the delta doesn't say. <paramref name="extruderTempsC"/> is per
    /// extruder too; a null entry leaves that extruder's temperature unchanged.
    /// </summary>
    public void RecordTelemetry(
        decimal? nozzleTempC,
        decimal? bedTempC,
        int? printProgressPercent,
        string? currentStageLabel,
        int? remainingTimeMinutes,
        string? currentJobName,
        IReadOnlyList<FeedUnitReading> feedReadings,
        int? currentLayer = null,
        int? totalLayers = null,
        IReadOnlyList<FeedSlotRef?>? activeSlots = null,
        string? currentGcodeFile = null,
        IReadOnlyList<decimal?>? extruderTempsC = null)
    {
        NozzleTempC = nozzleTempC ?? NozzleTempC;
        BedTempC = bedTempC ?? BedTempC;
        PrintProgressPercent = printProgressPercent ?? PrintProgressPercent;
        CurrentLayer = currentLayer ?? CurrentLayer;
        TotalLayers = totalLayers ?? TotalLayers;
        CurrentStageLabel = currentStageLabel ?? CurrentStageLabel;
        RemainingTimeMinutes = remainingTimeMinutes ?? RemainingTimeMinutes;
        CurrentJobName = currentJobName ?? CurrentJobName;
        CurrentGcodeFile = currentGcodeFile ?? CurrentGcodeFile;
        if (feedReadings.Count > 0)
        {
            var reportedKinds = feedReadings.Select(r => r.Kind).ToHashSet();
            FeedReadings = FeedReadings.Where(r => !reportedKinds.Contains(r.Kind)).Concat(feedReadings)
                .OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.UnitIndex).ToList();
        }

        if (activeSlots is not null)
            Extruders = WithExtruders(activeSlots.Count, (extruder, index) => extruder with { ActiveSlot = activeSlots[index] });
        if (extruderTempsC is not null)
            Extruders = WithExtruders(extruderTempsC.Count, (extruder, index) => extruder with { TempC = extruderTempsC[index] ?? extruder.TempC });
        MarkActiveFeed();
    }

    /// <summary>
    /// Which slot feeds each extruder and what the printer lists arrive in separate deltas, so the active flags are derived from
    /// the merged state: a slot is active when its extruder reports it as the one feeding it, and an extruder no multi-material
    /// unit feeds can only be fed by its external spool.
    /// </summary>
    private void MarkActiveFeed()
    {
        var marked = FeedReadings
            .Select(unit => unit with { Slots = unit.Slots.Select(slot => slot with { Active = IsActiveFeed(unit, slot) }).ToList() })
            .ToList();
        // Rebuild only on a real change, so the jsonb value comparer sees no change on an unchanged active slot.
        if (!marked.SelectMany(u => u.Slots).Select(s => s.Active).SequenceEqual(FeedReadings.SelectMany(u => u.Slots).Select(s => s.Active)))
            FeedReadings = marked;
    }

    private bool IsActiveFeed(FeedUnitReading unit, FeedSlotReading slot) =>
        unit.Kind == FeedUnitKindCatalog.ExternalSpool && !FeedReadings.Any(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit
            && u.Slots.Any(s => s.ExtruderIndex == slot.ExtruderIndex))
        || Extruders.FirstOrDefault(e => e.Index == slot.ExtruderIndex)?.ActiveSlot == new FeedSlotRef(unit.Kind, unit.UnitIndex, slot.SlotPosition);

    /// <summary>The extruder readings covering at least <paramref name="count"/> extruders, the first <paramref name="count"/> updated.</summary>
    private IReadOnlyList<ExtruderReading> WithExtruders(int count, Func<ExtruderReading, int, ExtruderReading> update)
    {
        var extruders = Enumerable.Range(0, Math.Max(count, Extruders.Count))
            .Select(index => Extruders.FirstOrDefault(e => e.Index == index) ?? new ExtruderReading(index))
            .Select(extruder => extruder.Index < count ? update(extruder, extruder.Index) : extruder)
            .ToList();
        // An unchanged report keeps the same list, so the jsonb value comparer sees no change.
        return extruders.SequenceEqual(Extruders) ? Extruders : extruders;
    }

    /// <summary>Clears the last print's progress when a new job starts, so a stale value is never read as the new job's.</summary>
    public void ResetPrintProgress()
    {
        PrintProgressPercent = null;
        CurrentLayer = null;
        TotalLayers = null;
    }

    /// <summary>
    /// Renames the printer. Orthogonal to <see cref="Status"/> — unlike <see cref="TransitionTo"/>,
    /// no state-machine gating applies; a printer may be renamed regardless of its current status
    /// (FR-001).
    /// </summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Printer name is required.", nameof(name));
        }

        Name = name;
    }

    public void EditConnection(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            throw new ArgumentException("Printer IP address is required.", nameof(ipAddress));
        }

        IpAddress = ipAddress;
        BeginConnection();
    }

    public void LinkMachineProfile(Guid machineProfileId) => MachineProfileId = machineProfileId;

    public void UnlinkMachineProfile() => MachineProfileId = null;

    public void LinkProcessProfile(Guid processProfileId) => ProcessProfileId = processProfileId;

    public void UnlinkProcessProfile() => ProcessProfileId = null;

    public void SelectBuildPlate(BuildPlateType buildPlateType) => BuildPlateType = buildPlateType;

    public void ClearBuildPlate() => BuildPlateType = null;
}
