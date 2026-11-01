namespace OpenFarm.Domain.Production;

/// <summary>
/// The exploded leaf demand — one row per distinct (ProductionDemand, Part) pair (spec Key Entities /
/// FR-004), snapshotted once and never re-derived (research.md R8). Carries no reference back to
/// the source Assembly/BomLine graph beyond <see cref="RunLineId"/> — this is what makes FR-003's
/// snapshot guarantee true by construction.
/// </summary>
public sealed class PartRequirement
{
    private PartRequirement(Guid id, Guid runLineId, Guid partId, decimal? quantity)
    {
        Id = id;
        RunLineId = runLineId;
        PartId = partId;
        LibraryPartId = partId;
        Quantity = quantity;
    }

    public Guid Id { get; }

    public decimal UnitsPerSet { get; private set; } = 1;
    public bool RecipeActive { get; private set; } = true;
    public void CaptureRecipeRatio(decimal units) => UnitsPerSet = units > 0 ? units : throw new ArgumentOutOfRangeException(nameof(units));
    public void RetireRecipe() => RecipeActive = false;

    /// <summary>Legacy storage name for the owning ProductionDemand ID; no Run is required.</summary>
    public Guid RunLineId { get; }

    public Guid PartId { get; }

    public Guid? LibraryPartId { get; private set; }

    public string? PartName { get; private set; }

    public void CaptureName(string name) => PartName ??= name;

    public void DetachLibraryPart() => LibraryPartId = null;

    /// <summary>Null mirrors the owning ProductionDemand's unlimited quantity; otherwise the summed exploded total (FR-004).</summary>
    public decimal? Quantity { get; }

    /// <summary>
    /// FR-016a: done when every lost plate was reprinted or written off and completed plus written-off units reach the
    /// target. An unlimited requirement never holds its run open.
    /// </summary>
    public bool IsSatisfiedBy(IReadOnlyCollection<PrintJob> jobs) =>
        Quantity is not { } target
        || (!jobs.Any(j => j.IsUnresolvedLoss)
            && jobs.Sum(j => j.ProducedUnits + j.WrittenOffUnits) >= target);

    /// <summary>
    /// A standalone aggregate root, not a <see cref="ProductionRun"/>/<see cref="ProductionDemand"/> child
    /// collection (mirrors <c>Spool.Create</c>'s own standalone-root shape, not <c>BomLine.Create</c>'s
    /// owned-child one) — deliberately public: resolving a ProductionDemand's exploded part demand
    /// (<c>IAssemblyGraphQueryService.ExplodeAsync</c>, research.md R1) is an async, DB-backed step
    /// no synchronous Domain factory could perform, so the Application-layer command handler that
    /// awaits it is necessarily this type's only caller.
    /// </summary>
    public static PartRequirement Create(Guid runLineId, Guid partId, decimal? quantity) =>
        new(Guid.NewGuid(), runLineId, partId, quantity);
}
