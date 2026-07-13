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

    public IReadOnlyList<AmsUnit> AmsUnits { get; private set; }

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
        IReadOnlyList<AmsUnit> amsUnits)
    {
        NozzleTempC = nozzleTempC ?? NozzleTempC;
        BedTempC = bedTempC ?? BedTempC;
        PrintProgressPercent = printProgressPercent ?? PrintProgressPercent;
        CurrentStageLabel = currentStageLabel ?? CurrentStageLabel;
        AmsUnits = amsUnits.Count > 0 ? amsUnits : AmsUnits;
    }

    public void EditConnection(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            throw new ArgumentException("Printer IP address is required.", nameof(ipAddress));
        }

        IpAddress = ipAddress;
    }
}
