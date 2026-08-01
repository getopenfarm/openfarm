namespace OpenFarm.Domain.Printers;

public sealed class Printer
{
    private Printer(
        Guid id,
        Guid organizationId,
        string name,
        string brand,
        string modelCode,
        string ipAddress,
        string serialNumber,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Brand = brand;
        ModelCode = modelCode;
        IpAddress = ipAddress;
        SerialNumber = serialNumber;
        Status = PrinterStatus.Idle;
        IsReachable = false;
        AmsUnits = [];
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public string Name { get; private set; }

    public string Brand { get; private set; }

    public string ModelCode { get; private set; }

    public string IpAddress { get; private set; }

    public string SerialNumber { get; }

    public PrinterStatus Status { get; private set; }

    public bool IsReachable { get; private set; }

    public DateTimeOffset? LastMessageUtc { get; private set; }

    public decimal? NozzleTempC { get; private set; }

    public decimal? BedTempC { get; private set; }

    public int? PrintProgressPercent { get; private set; }

    public string? CurrentStageLabel { get; private set; }

    public int? RemainingTimeMinutes { get; private set; }

    public string? CurrentJobName { get; private set; }

    public IReadOnlyList<AmsUnit> AmsUnits { get; private set; }

    public AmsTray? ExternalSpoolTray { get; private set; }

    /// <summary>
    /// Optional link to a Machine Profile (Phase 2), filling the deferred link `docs/FEATURE_PLAN.md`'s
    /// own Phase 1 notes earmarked (FR-005b). `null` at creation time — a matching profile might not be
    /// imported yet. Only a printer with this set counts as a "real fleet member" for
    /// <c>NozzleDiameterResolver</c> (FR-005a).
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
        Guid organizationId,
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
        PrinterModelCatalog.Resolve(brand, modelCode);

        return new Printer(Guid.NewGuid(), organizationId, name, brand, modelCode, ipAddress, serialNumber, DateTimeOffset.UtcNow);
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
        if (observed is null || observed == Status)
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
    }

    /// <summary>
    /// Bambu's MQTT reports are deltas (research.md §1): a missing field means "unchanged", not
    /// "cleared". Each nullable parameter therefore falls back to the previously recorded value
    /// rather than overwriting it with null. <paramref name="amsUnits"/> can't represent "absent"
    /// at the type level (the parser returns an empty list both when the AMS key is missing from
    /// a delta and when it's genuinely empty), so an empty list is likewise treated as "unchanged".
    /// </summary>
    public void RecordTelemetry(
        decimal? nozzleTempC,
        decimal? bedTempC,
        int? printProgressPercent,
        string? currentStageLabel,
        int? remainingTimeMinutes,
        string? currentJobName,
        IReadOnlyList<AmsUnit> amsUnits,
        AmsTray? externalSpoolTray = null)
    {
        NozzleTempC = nozzleTempC ?? NozzleTempC;
        BedTempC = bedTempC ?? BedTempC;
        PrintProgressPercent = printProgressPercent ?? PrintProgressPercent;
        CurrentStageLabel = currentStageLabel ?? CurrentStageLabel;
        RemainingTimeMinutes = remainingTimeMinutes ?? RemainingTimeMinutes;
        CurrentJobName = currentJobName ?? CurrentJobName;
        AmsUnits = amsUnits.Count > 0 ? amsUnits : AmsUnits;
        ExternalSpoolTray = externalSpoolTray ?? ExternalSpoolTray;
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
    }

    public void LinkMachineProfile(Guid machineProfileId) => MachineProfileId = machineProfileId;

    public void UnlinkMachineProfile() => MachineProfileId = null;

    public void LinkProcessProfile(Guid processProfileId) => ProcessProfileId = processProfileId;

    public void UnlinkProcessProfile() => ProcessProfileId = null;

    public void SelectBuildPlate(BuildPlateType buildPlateType) => BuildPlateType = buildPlateType;

    public void ClearBuildPlate() => BuildPlateType = null;
}
