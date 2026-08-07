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
public sealed class PrintJob
{
    private readonly List<JobFilamentConsumption> _filamentConsumption = [];

    private PrintJob(
        Guid id, Guid organizationId, Guid partRequirementId, double queuePosition, int plateQuantity,
        AssignmentMode assignmentMode, Guid? pinnedPrinterId, string fileRevisionHash, int fileRevisionNumber,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        PartRequirementId = partRequirementId;
        QueuePosition = queuePosition;
        PlateQuantity = plateQuantity;
        AssignmentMode = assignmentMode;
        PinnedPrinterId = pinnedPrinterId;
        Status = PrintJobStatus.Queued;
        FileRevisionHash = fileRevisionHash;
        FileRevisionNumber = fileRevisionNumber;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid PartRequirementId { get; }

    /// <summary>Fractional index (research.md R3) — a reorder touches only the moved job's own row.</summary>
    public double QueuePosition { get; private set; }

    /// <summary>== Part.UnitsPerPrint at creation time — informational, this plate's target count.</summary>
    public int PlateQuantity { get; }

    public AssignmentMode AssignmentMode { get; private set; }

    /// <summary>Set iff <see cref="AssignmentMode"/> == <see cref="Production.AssignmentMode.Pinned"/> (FR-007c).</summary>
    public Guid? PinnedPrinterId { get; private set; }

    /// <summary>Set by the dispatcher once actually sent (either assignment mode).</summary>
    public Guid? DispatchedPrinterId { get; private set; }

    public PrintJobStatus Status { get; private set; }

    /// <summary>FR-009c — copied from Part at queue (Snapshot) or dispatch (Follow-latest) time.</summary>
    public string FileRevisionHash { get; private set; }

    public int FileRevisionNumber { get; private set; }

    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>FR-015, derived from Started/EndedAtUtc on close.</summary>
    public int? ActualDurationMinutes { get; private set; }

    /// <summary>FR-024a/c — set when a stop is requested through OpenFarm; distinguishes Cancelled from Failed.</summary>
    public DateTimeOffset? CancelRequestedAtUtc { get; private set; }

    /// <summary>FR-025a — set once a replacement job has been created for this one.</summary>
    public Guid? RequeuedAsJobId { get; private set; }

    public IReadOnlyList<JobFilamentConsumption> FilamentConsumption => _filamentConsumption;

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// A standalone aggregate root, not a <see cref="ProductionRun"/>/<see cref="PartRequirement"/>
    /// child collection — see <see cref="PartRequirement.Create"/>'s own remarks on why (eager
    /// creation for a finite requirement, research.md R2, is an Application-layer command handler
    /// concern, not something a synchronous Domain factory on some other aggregate could reach into).
    /// </summary>
    public static PrintJob Create(
        Guid organizationId, Guid partRequirementId, double queuePosition, int plateQuantity,
        AssignmentMode assignmentMode, Guid? pinnedPrinterId, string fileRevisionHash, int fileRevisionNumber,
        IReadOnlyList<JobFilamentRequirementSpec> filamentRequirements)
    {
        if (assignmentMode == AssignmentMode.Pinned && pinnedPrinterId is null)
        {
            throw new PinnedJobRequiresPrinterException();
        }

        var effectivePinnedPrinterId = assignmentMode == AssignmentMode.Pinned ? pinnedPrinterId : null;

        var job = new PrintJob(
            Guid.NewGuid(), organizationId, partRequirementId, queuePosition, plateQuantity, assignmentMode,
            effectivePinnedPrinterId, fileRevisionHash, fileRevisionNumber, DateTimeOffset.UtcNow);

        foreach (var requirement in filamentRequirements)
        {
            job._filamentConsumption.Add(
                JobFilamentConsumption.Create(organizationId, job.Id, requirement.MaterialType, requirement.DetectedColorHex));
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
        DispatchedPrinterId = printerId;
        DispatchedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Follow-latest mode — re-reads the Part's current revision immediately before send (research.md R6). Never called once a job leaves Queued.</summary>
    public void Dispatch(Guid printerId, string fileRevisionHash, int fileRevisionNumber)
    {
        EnsureNotYetDispatched();
        DispatchedPrinterId = printerId;
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
        DispatchedPrinterId = null;
        DispatchedAtUtc = null;
    }

    /// <summary>FR-024a — sets only CancelRequestedAtUtc; never a status transition itself. Idempotent.</summary>
    public void RequestCancel() => CancelRequestedAtUtc ??= DateTimeOffset.UtcNow;

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

    public void MarkFailed(string? reason)
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Failed);
        Close(PrintJobStatus.Failed);
    }

    public void MarkCompleted()
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Completed);
        Close(PrintJobStatus.Completed);
    }

    /// <summary>See FR-024c for how this is chosen over MarkFailed.</summary>
    public void MarkCancelled()
    {
        PrintJobStatusTransitions.EnsureValidTransition(Status, PrintJobStatus.Cancelled);
        Close(PrintJobStatus.Cancelled);
    }

    /// <summary>FR-025a — one-time set, throws if already set.</summary>
    public void MarkRequeuedAs(Guid replacementJobId)
    {
        if (RequeuedAsJobId is not null)
        {
            throw new PrintJobAlreadyRequeuedException(Id);
        }

        RequeuedAsJobId = replacementJobId;
    }

    /// <summary>FR-013/FR-013a, callable on any terminal transition.</summary>
    public void RecordFilamentConsumption(Guid consumptionLineId, Guid? spoolId, decimal gramsConsumed)
    {
        var line = _filamentConsumption.FirstOrDefault(l => l.Id == consumptionLineId)
            ?? throw new FilamentConsumptionSumMismatchException(null, null);
        line.Record(spoolId, gramsConsumed);
    }

    private void Close(PrintJobStatus terminalStatus)
    {
        Status = terminalStatus;
        EndedAtUtc = DateTimeOffset.UtcNow;
        if (StartedAtUtc is not null)
        {
            ActualDurationMinutes = (int)Math.Round((EndedAtUtc.Value - StartedAtUtc.Value).TotalMinutes);
        }
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
