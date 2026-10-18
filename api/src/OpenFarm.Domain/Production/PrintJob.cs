namespace OpenFarm.Domain.Production;

/// <summary>
/// The execution unit (spec Key Entities) — flat and atomic, unlike the hierarchical demand above
/// it. <see cref="MarkStarted"/>/<see cref="MarkPaused"/>/<see cref="MarkResumed"/>/
/// <see cref="MarkCompleted"/>/<see cref="MarkFailed"/>/<see cref="MarkCancelled"/> are called from
/// exactly one place per printer kind, never both, for a given job (research.md R12): the sole
/// caller for a live-integrated printer is <c>IJobTelemetrySync</c>, driven off the printer's own
/// reported state; for a manual-brand printer it's the matching Application command handler,
/// trusting the operator's own report directly.
/// </summary>
public sealed class PrintJob : Print
{
    private PrintJob(
        Guid id, string name, Guid partRequirementId, double queuePosition, int plateQuantity,
        AssignmentMode assignmentMode, Guid? pinnedPrinterId, string fileRevisionHash, int fileRevisionNumber,
        DateTimeOffset createdAtUtc)
        : base(id, name, PrintSource.Dispatched, PrintJobStatus.Queued, createdAtUtc)
    {
        PartRequirementId = partRequirementId;
        QueuePosition = queuePosition;
        PlateQuantity = plateQuantity;
        AssignmentMode = assignmentMode;
        PinnedPrinterId = pinnedPrinterId;
        FileRevisionHash = fileRevisionHash;
        FileRevisionNumber = fileRevisionNumber;
    }

    public string? ProductionSnapshotJson { get; private set; }

    public string? SnapshotRecoveryReason { get; private set; }

    public DateTimeOffset? SnapshotUnavailableAtUtc { get; private set; }

    public string GetSnapshotState() => ProductionSnapshotJson is not null ? "Ready"
        : SnapshotUnavailableAtUtc is not null ? "Unavailable" : "Unresolved";

    public void MarkSnapshotUnavailable(DateTimeOffset removedAtUtc)
    {
        if (ProductionSnapshotJson is not null || SnapshotUnavailableAtUtc is not null) return;
        if (Status is not (PrintJobStatus.Cancelled or PrintJobStatus.Failed or PrintJobStatus.Completed))
            throw new InvalidOperationException("Unfinished jobs must retain their production source.");
        SnapshotUnavailableAtUtc = removedAtUtc;
        SnapshotRecoveryReason = "Source part removed; retry and file download are unavailable.";
    }

    public OpenFarm.Domain.Parts.Part? GetProductionPart() => ProductionSnapshotJson is null ? null
        : OpenFarm.Domain.Parts.Part.FromProductionSnapshot(
            System.Text.Json.JsonSerializer.Deserialize<ProductionPartSnapshot>(ProductionSnapshotJson)!);

    public void CaptureProductionSnapshot(OpenFarm.Domain.Parts.Part part, OpenFarm.Domain.Parts.SlicerRef? slicer = null)
    {
        if (SnapshotUnavailableAtUtc is not null)
            throw new ProductionSnapshotUnavailableException(Id, SnapshotRecoveryReason);
        if (ProductionSnapshotJson is not null) return;
        part.EnsurePrintRequirementsComplete();
        if (part.RevisionHash != FileRevisionHash || part.RevisionNumber != FileRevisionNumber)
            throw new InvalidOperationException("The production snapshot must match the job revision.");
        ProductionSnapshotJson = System.Text.Json.JsonSerializer.Serialize(ProductionPartSnapshot.Capture(part, slicer));
        SnapshotRecoveryReason = null;
    }

    public void CopyProductionSnapshot(PrintJob source)
    {
        if (SnapshotUnavailableAtUtc is not null)
            throw new ProductionSnapshotUnavailableException(Id, SnapshotRecoveryReason);
        if (source.ProductionSnapshotJson is null)
            throw new InvalidOperationException("The original job snapshot needs recovery before requeueing.");
        ProductionSnapshotJson = source.ProductionSnapshotJson;
        SnapshotRecoveryReason = null;
    }

    public void ResetUnsentBackfillSnapshot(OpenFarm.Domain.Parts.Part part)
    {
        if (SnapshotUnavailableAtUtc is not null)
            throw new ProductionSnapshotUnavailableException(Id, SnapshotRecoveryReason);
        EnsureQueued();
        if (DispatchedAtUtc is not null) throw new InvalidOperationException("A dispatched snapshot is immutable.");
        ProductionSnapshotJson = null;
        FileRevisionHash = part.RevisionHash;
        FileRevisionNumber = part.RevisionNumber;
    }

    public void MarkSnapshotUnresolved(string reason)
    {
        if (SnapshotUnavailableAtUtc is null) SnapshotRecoveryReason = reason;
    }

    public Guid PartRequirementId { get; }

    /// <summary>Fractional index (research.md R3) — a reorder touches only the moved job's own row.</summary>
    public double QueuePosition { get; private set; }

    /// <summary>== Part.UnitsPerPrint at creation time — informational, this plate's target count.</summary>
    public int PlateQuantity { get; }

    public AssignmentMode AssignmentMode { get; private set; }

    /// <summary>Set iff <see cref="AssignmentMode"/> == <see cref="Production.AssignmentMode.Pinned"/> (FR-007c).</summary>
    public Guid? PinnedPrinterId { get; private set; }

    /// <summary>FR-009c — copied from Part at queue (Snapshot) or dispatch (Follow-latest) time.</summary>
    public string FileRevisionHash { get; private set; }

    public int FileRevisionNumber { get; private set; }

    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    /// <summary>FR-024a/c — set when a stop is requested through OpenFarm; distinguishes Cancelled from Failed.</summary>
    public DateTimeOffset? CancelRequestedAtUtc { get; private set; }

    /// <summary>FR-025a — set once a replacement job has been created for this one.</summary>
    public Guid? RequeuedAsJobId { get; private set; }

    public bool HasFilamentRequirementOverride { get; private set; }

    /// <summary>
    /// A standalone aggregate root, not a <see cref="ProductionRun"/>/<see cref="PartRequirement"/>
    /// child collection — see <see cref="PartRequirement.Create"/>'s own remarks on why (eager
    /// creation for a finite requirement, research.md R2, is an Application-layer command handler
    /// concern, not something a synchronous Domain factory on some other aggregate could reach into).
    /// </summary>
    public static PrintJob Create(
        string name, Guid partRequirementId, double queuePosition, int plateQuantity,
        AssignmentMode assignmentMode, Guid? pinnedPrinterId, string fileRevisionHash, int fileRevisionNumber,
        IReadOnlyList<JobFilamentRequirementSpec> filamentRequirements)
    {
        if (assignmentMode == AssignmentMode.Pinned && pinnedPrinterId is null)
        {
            throw new PinnedJobRequiresPrinterException();
        }

        var effectivePinnedPrinterId = assignmentMode == AssignmentMode.Pinned ? pinnedPrinterId : null;

        var job = new PrintJob(
            Guid.NewGuid(), name, partRequirementId, queuePosition, plateQuantity, assignmentMode,
            effectivePinnedPrinterId, fileRevisionHash, fileRevisionNumber, DateTimeOffset.UtcNow);

        for (var i = 0; i < filamentRequirements.Count; i++)
        {
            job.AddFilament(PrintFilament.Create(job.Id, i, filamentRequirements[i]));
        }

        return job;
    }

    /// <summary>Operator action, FR-007. Only valid pre-dispatch; throws PinnedJobRequiresPrinterException if printerId is missing, mirroring Create's own guard.</summary>
    public void Pin(Guid? printerId)
    {
        EnsureQueued();
        if (printerId is null)
        {
            throw new PinnedJobRequiresPrinterException();
        }

        AssignmentMode = AssignmentMode.Pinned;
        PinnedPrinterId = printerId;
    }

    /// <summary>-> Unassigned, eligible for ordinary dispatch matching again. Only valid pre-dispatch.</summary>
    public void Unpin()
    {
        EnsureQueued();
        AssignmentMode = AssignmentMode.Unassigned;
        PinnedPrinterId = null;
    }

    /// <summary>FR-008 — the fractional-index move (research.md R3). Only valid pre-dispatch.</summary>
    public void Reorder(double newQueuePosition)
    {
        EnsureQueued();
        QueuePosition = newQueuePosition;
    }

    /// <summary>Snapshot mode — the revision recorded at Create time is never touched (research.md R6).</summary>
    public void Dispatch(Guid printerId)
    {
        EnsureNotYetDispatched();
        PrinterId = printerId;
        DispatchedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Follow-latest mode — re-reads the Part's current revision immediately before send (research.md R6). Never called once a job leaves Queued.</summary>
    public void Dispatch(Guid printerId, string fileRevisionHash, int fileRevisionNumber)
    {
        EnsureNotYetDispatched();
        PrinterId = printerId;
        DispatchedAtUtc = DateTimeOffset.UtcNow;
        FileRevisionHash = fileRevisionHash;
        FileRevisionNumber = fileRevisionNumber;
    }

    /// <summary>
    /// Constitution Durable Recovery guardrail — clears a dispatch-in-flight marker left by a prior
    /// process lifetime (<see cref="DispatchedAtUtc"/> set, telemetry never confirmed <c>Printing</c>),
    /// so the job re-enters ordinary matching exactly as if never dispatched. A no-op if the job was
    /// never in that window (idempotent, safe to call speculatively during reconciliation).
    /// </summary>
    public void RevertDispatch()
    {
        PrinterId = null;
        DispatchedAtUtc = null;
    }

    /// <summary>FR-024a — sets only CancelRequestedAtUtc; never a status transition itself. Idempotent.</summary>
    public void RequestCancel() => CancelRequestedAtUtc ??= DateTimeOffset.UtcNow;

    /// <summary>FR-025a — one-time set, throws if already set.</summary>
    public void MarkRequeuedAs(Guid replacementJobId)
    {
        if (RequeuedAsJobId is not null)
        {
            throw new PrintJobAlreadyRequeuedException(Id);
        }

        RequeuedAsJobId = replacementJobId;
    }

    public void SetFilamentRequirements(IReadOnlyList<JobFilamentRequirementSpec> requirements, bool isOverride = true)
    {
        EnsureQueued();
        if (requirements.Count == 0) throw new FilamentConsumptionSumMismatchException(null, null);
        HasFilamentRequirementOverride = isOverride;
        ClearFilaments();
        for (var i = 0; i < requirements.Count; i++)
            AddFilament(PrintFilament.Create(Id, i, requirements[i]));
    }

    public PrintFilament AddConsumptionLine(string? materialType, string? colorHex)
    {
        var line = PrintFilament.Create(Id, Filaments.Count, materialType, colorHex);
        AddFilament(line);
        return line;
    }

    private void EnsureQueued()
    {
        if (Status != PrintJobStatus.Queued || DispatchedAtUtc is not null)
        {
            throw new PrintJobAlreadyDispatchedException(Id);
        }
    }

    private void EnsureNotYetDispatched()
    {
        if (Status != PrintJobStatus.Queued || DispatchedAtUtc is not null)
        {
            throw new PrintJobAlreadyDispatchedException(Id);
        }
    }
}
