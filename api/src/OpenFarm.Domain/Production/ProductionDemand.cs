namespace OpenFarm.Domain.Production;

/// <summary>
/// Standalone scheduling demand for one Part or Assembly, optionally grouped in a Production Run.
/// Quantity basis is explicit; finite work snapshots inputs and backfill follows current Part revisions.
/// </summary>
public sealed class ProductionDemand
{
    private ProductionDemand(
        Guid id, Guid? productionRunId, RunLineTargetKind targetKind, Guid targetId,
        int? quantity, RevisionMode revisionMode, AssignmentMode defaultAssignmentMode, Guid? defaultPinnedPrinterId,
        int backfillPrinterCap, int? backfillCumulativeUnitCeiling)
    {
        Id = id;
        ProductionRunId = productionRunId;
        TargetKind = targetKind;
        TargetId = targetId;
        Quantity = quantity;
        RevisionMode = revisionMode;
        DefaultAssignmentMode = defaultAssignmentMode;
        DefaultPinnedPrinterId = defaultPinnedPrinterId;
        BackfillPrinterCap = backfillPrinterCap;
        BackfillCumulativeUnitCeiling = backfillCumulativeUnitCeiling;
    }

    public Guid Id { get; }

    public DemandMode Mode { get; private set; }
    public QuantityBasis QuantityBasis { get; private set; }
    public DemandState State { get; private set; } = DemandState.Enabled;
    public Guid? SubmissionId { get; private set; }
    public string? SubmissionRequestJson { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset BalanceSinceUtc { get; private set; } = DateTimeOffset.UtcNow;

    public void SetSubmissionId(Guid id, string requestJson)
    {
        SubmissionId = id;
        SubmissionRequestJson = requestJson;
    }

    public void SetState(DemandState state)
    {
        if (Mode != DemandMode.Backfill || State == DemandState.Removed)
            throw new InvalidOperationException("Only existing backfill demand can change state.");
        State = state;
    }

    public void ConfigureAssignment(Guid? printerId)
    {
        DefaultPinnedPrinterId = printerId;
        DefaultAssignmentMode = printerId is null ? AssignmentMode.Unassigned : AssignmentMode.Pinned;
    }

    public void RefreshRecipe() => BalanceSinceUtc = DateTimeOffset.UtcNow;


    public Guid? ProductionRunId { get; }

    public RunLineTargetKind TargetKind { get; }

    public Guid TargetId { get; }

    public string? TargetName { get; private set; }

    public void CaptureTargetName(string name) => TargetName ??= name;

    /// <summary>The requested count in QuantityBasis; null for continuous demand. Mode is the scheduling discriminator.</summary>
    public int? Quantity { get; }

    public RevisionMode RevisionMode { get; private set; }

    public AssignmentMode DefaultAssignmentMode { get; private set; }

    /// <summary>Set iff <see cref="DefaultAssignmentMode"/> == <see cref="AssignmentMode.Pinned"/>.</summary>
    public Guid? DefaultPinnedPrinterId { get; private set; }

    /// <summary>Meaningful only when <see cref="Quantity"/> is null; default 1 (FR-006c).</summary>
    public int BackfillPrinterCap { get; private set; }

    /// <summary>Meaningful only when <see cref="Quantity"/> is null; null = no ceiling (FR-006d).</summary>
    public int? BackfillCumulativeUnitCeiling { get; private set; }

    public static ProductionDemand Create(Guid? productionRunId, ProductionDemandSpec spec)
    {
        if (spec.Quantity is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "Quantity must be at least 1, or null for unlimited.");
        }

        if (spec.DefaultAssignmentMode == AssignmentMode.Pinned && spec.DefaultPinnedPrinterId is null)
        {
            throw new PinnedJobRequiresPrinterException();
        }

        if (spec.BackfillPrinterCap < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "Backfill printer cap must be at least 1.");
        }

        // Quantity fixes revision behavior: finite snapshots and unlimited library demand.
        var revisionMode = spec.Quantity is null ? Production.RevisionMode.FollowLatest : Production.RevisionMode.Snapshot;
        if (spec.RevisionMode is { } requested && requested != revisionMode)
            throw new InvalidRevisionModeException();

        var demand = new ProductionDemand(
            Guid.NewGuid(), productionRunId, spec.TargetKind, spec.TargetId, spec.Quantity,
            revisionMode, spec.DefaultAssignmentMode, spec.DefaultPinnedPrinterId, spec.BackfillPrinterCap,
            spec.BackfillCumulativeUnitCeiling);
        demand.Mode = spec.Quantity is null ? DemandMode.Backfill : DemandMode.Finite;
        demand.QuantityBasis = spec.QuantityBasis ?? (spec.TargetKind == RunLineTargetKind.Assembly ? QuantityBasis.Sets : QuantityBasis.Units);
        if (spec.TargetKind == RunLineTargetKind.Assembly && demand.QuantityBasis != QuantityBasis.Sets
            || spec.TargetKind == RunLineTargetKind.Part && demand.QuantityBasis == QuantityBasis.Sets)
            throw new ArgumentException("Quantity basis does not match the target.");
        return demand;
    }

    /// <summary>Its pinned printer is gone: the line's later jobs go to whichever printer matches.</summary>
    public void Unpin()
    {
        DefaultAssignmentMode = AssignmentMode.Unassigned;
        DefaultPinnedPrinterId = null;
    }

    /// <summary>FR-006c/d — meaningful only on an unlimited line; the caller enforces that (PrintJobNotEligibleForRequeueException's sibling check lives at the command handler).</summary>
    public void SetBackfillLimits(int printerCap, int? cumulativeUnitCeiling)
    {
        if (printerCap < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(printerCap), "Backfill printer cap must be at least 1.");
        }

        BackfillPrinterCap = printerCap;
        BackfillCumulativeUnitCeiling = cumulativeUnitCeiling;
    }
}
