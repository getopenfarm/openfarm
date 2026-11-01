namespace OpenFarm.Domain.Production;

/// <summary>
/// The demand container (spec Key Entities). <see cref="Name"/>/<see cref="DueDate"/>/<see cref="Notes"/> are optional
/// display metadata — none feeds the dispatcher (FR-008a, research.md R3); a run moves ahead only by queue order.
/// </summary>
public sealed class ProductionRun
{
    private readonly List<ProductionDemand> _lines = [];

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

    /// <summary>Legacy free text from before runs moved by queue order alone; no longer set or shown.</summary>
    public string? Priority { get; private set; }

    public string? Notes { get; private set; }

    public ProductionRunStatus Status { get; private set; }

    public IReadOnlyList<ProductionDemand> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsOpen => Status is ProductionRunStatus.Active or ProductionRunStatus.Complete;

    public static ProductionRun Create(string? name, DateOnly? dueDate, string? notes, IReadOnlyList<ProductionDemandSpec> lines)
    {
        if (lines.Count == 0)
        {
            throw new ProductionRunMustHaveAtLeastOneLineException();
        }

        var run = new ProductionRun(Guid.NewGuid(), BlankToNull(name), dueDate, null, BlankToNull(notes), DateTimeOffset.UtcNow);
        foreach (var spec in lines)
        {
            run._lines.Add(ProductionDemand.Create(run.Id, spec));
        }

        return run;
    }

    public void UpdateDetails(string? name, DateOnly? dueDate, string? notes)
    {
        EnsureOpen();
        Name = BlankToNull(name);
        DueDate = dueDate;
        Notes = BlankToNull(notes);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>More demand on an open run; a Complete run is Active again until the new plates are done.</summary>
    public IReadOnlyList<ProductionDemand> AddLines(IReadOnlyList<ProductionDemandSpec> lines)
    {
        EnsureOpen();
        if (lines.Count == 0)
        {
            throw new ProductionRunMustHaveAtLeastOneLineException();
        }

        var added = lines.Select(spec => ProductionDemand.Create(Id, spec)).ToList();
        _lines.AddRange(added);
        Status = ProductionRunStatus.Active;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return added;
    }

    /// <summary>
    /// FR-016a — every finite requirement satisfied. The entity has no visibility into PartRequirement/PrintJob state
    /// (separate aggregates), so the Application layer decides when to call this; it only enforces the transition.
    /// </summary>
    public void MarkComplete()
    {
        if (Status != ProductionRunStatus.Active)
        {
            throw new InvalidRunCompletionException(Id);
        }

        Status = ProductionRunStatus.Complete;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The operator's sign-off, reaching the terminal state. From Complete, or from Active when no finite work
    /// remains — a run of unlimited lines only, which never completes on its own.
    /// </summary>
    public void Close(bool finiteWorkRemains)
    {
        if (Status != ProductionRunStatus.Complete && (Status != ProductionRunStatus.Active || finiteWorkRemains))
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

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new ProductionRunNotOpenException(Id);
        }
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
