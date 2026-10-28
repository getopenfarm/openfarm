namespace OpenFarm.Domain.Production;

/// <summary>
/// One requested thing on a run (spec Key Entities / FR-002) — a Part or an Assembly, never both,
/// plus either a finite quantity or <see langword="null"/> for unlimited (backfill).
/// </summary>
public sealed class RunLine
{
    private RunLine(
        Guid id, Guid productionRunId, RunLineTargetKind targetKind, Guid targetId,
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

    public Guid ProductionRunId { get; }

    public RunLineTargetKind TargetKind { get; }

    public Guid TargetId { get; }

    public string? TargetName { get; private set; }

    public void CaptureTargetName(string name) => TargetName ??= name;

    /// <summary>Null means unlimited/backfill (FR-006) — the discriminator lives in the value, not a second flag.</summary>
    public int? Quantity { get; }

    public RevisionMode RevisionMode { get; private set; }

    public AssignmentMode DefaultAssignmentMode { get; private set; }

    /// <summary>Set iff <see cref="DefaultAssignmentMode"/> == <see cref="AssignmentMode.Pinned"/>.</summary>
    public Guid? DefaultPinnedPrinterId { get; private set; }

    /// <summary>Meaningful only when <see cref="Quantity"/> is null; default 1 (FR-006c).</summary>
    public int BackfillPrinterCap { get; private set; }

    /// <summary>Meaningful only when <see cref="Quantity"/> is null; null = no ceiling (FR-006d).</summary>
    public int? BackfillCumulativeUnitCeiling { get; private set; }

    internal static RunLine Create(Guid productionRunId, RunLineSpec spec)
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

        return new RunLine(
            Guid.NewGuid(), productionRunId, spec.TargetKind, spec.TargetId, spec.Quantity,
            revisionMode, spec.DefaultAssignmentMode, spec.DefaultPinnedPrinterId, spec.BackfillPrinterCap,
            spec.BackfillCumulativeUnitCeiling);
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
