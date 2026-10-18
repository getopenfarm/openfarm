namespace OpenFarm.Domain.Production;

/// <summary>
/// The demand container (spec Key Entities). <see cref="Name"/>/<see cref="DueDate"/>/
/// <see cref="Priority"/> are all optional display metadata — none of the three feed the
/// dispatcher (FR-008a, research.md R3).
/// </summary>
public sealed class ProductionRun
{
    private readonly List<RunLine> _lines = [];

    private ProductionRun(
        Guid id, string? name, DateOnly? dueDate, string? priority, string? notes,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        DueDate = dueDate;
        Priority = priority;
        Notes = notes;
        Status = ProductionRunStatus.Active;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    /// <summary>Null is a fully valid, fully usable run (FR-001a) — display-only labeling differs, nothing else.</summary>
    public string? Name { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public string? Priority { get; private set; }

    public string? Notes { get; private set; }

    public ProductionRunStatus Status { get; private set; }

    public IReadOnlyList<RunLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ProductionRun Create(
        string? name, DateOnly? dueDate, string? priority, string? notes, IReadOnlyList<RunLineSpec> lines)
    {
        if (lines.Count == 0)
        {
            throw new ProductionRunMustHaveAtLeastOneLineException();
        }

        var run = new ProductionRun(Guid.NewGuid(), name, dueDate, priority, notes, DateTimeOffset.UtcNow);
        foreach (var spec in lines)
        {
            run._lines.Add(RunLine.Create(run.Id, spec));
        }

        return run;
    }

    /// <summary>
    /// FR-016a — every finite requirement satisfied. The entity has no visibility into
    /// PartRequirement/PrintJob completion state (separate aggregates), so the caller (Application
    /// layer, which can query them) determines <paramref name="allFiniteRequirementsSatisfied"/>
    /// and this method only enforces the resulting invariant plus the legal status transition.
    /// </summary>
    public void MarkComplete(bool allFiniteRequirementsSatisfied)
    {
        if (Status != ProductionRunStatus.Active)
        {
            throw new InvalidRunCompletionException(Id);
        }

        if (!allFiniteRequirementsSatisfied)
        {
            throw new InvalidRunCompletionException(Id);
        }

        Status = ProductionRunStatus.Complete;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The explicit operator action reaching the terminal state (spec Assumptions). Only valid from Complete (FR-016c).</summary>
    public void Close()
    {
        if (Status != ProductionRunStatus.Complete)
        {
            throw new InvalidRunCompletionException(Id);
        }

        Status = ProductionRunStatus.Closed;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>FR-017 — Active/Complete -> Cancelled.</summary>
    public void Cancel()
    {
        if (Status is not (ProductionRunStatus.Active or ProductionRunStatus.Complete))
        {
            throw new InvalidRunCompletionException(Id);
        }

        Status = ProductionRunStatus.Cancelled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
