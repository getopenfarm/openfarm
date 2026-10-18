namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// A named description of a finished product (spec Key Entities) — one or more polymorphic
/// <see cref="BomLine"/>s, each targeting a Part, a sub-Assembly, or a <see cref="NonPrintedComponent"/>.
/// No status, no stock, no printer/job/run relationship — deliberately nothing beyond
/// <see cref="Name"/>/<see cref="Description"/>/<see cref="Lines"/> (data-model.md).
/// </summary>
public sealed class Assembly
{
    private readonly List<BomLine> _lines = [];

    private Assembly(Guid id, string name, string? description, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public IReadOnlyList<BomLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>FR-002: requires at least one line. Cycle/depth validation is the caller's job (AssemblyGraph, data-model.md) — this factory has no access to the full edge graph.</summary>
    public static Assembly Create(string name, string? description, IReadOnlyList<BomLineSpec> lines)
    {
        ValidateName(name);

        if (lines.Count == 0)
        {
            throw new AssemblyMustHaveAtLeastOneLineException();
        }

        var assembly = new Assembly(Guid.NewGuid(), name, description, DateTimeOffset.UtcNow);

        foreach (var spec in lines)
        {
            var line = BomLine.Create(assembly.Id, spec);
            EnsureNoDuplicateTarget(assembly._lines, line);
            assembly._lines.Add(line);
        }

        return assembly;
    }

    public void Rename(string name, string? description)
    {
        ValidateName(name);

        Name = name;
        Description = description;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Full replace of <see cref="Lines"/>, validated before anything mutates: kept lines (by id) take
    /// the new quantity, lines not listed are removed, and specs without an id are added. Swapping
    /// every old line for new ones is fine — only an empty result is rejected (FR-002). FR-013 duplicate
    /// targets are rejected here. Returns the added lines; cycle/depth validation of Assembly-kind ones is the caller's job.
    /// </summary>
    public IReadOnlyList<BomLine> ReplaceLines(IReadOnlyList<BomLineEdit> edits)
    {
        if (edits.Count == 0)
        {
            throw new AssemblyMustHaveAtLeastOneLineException();
        }

        var kept = new List<(BomLine Line, decimal Quantity)>();
        var added = new List<BomLine>();
        var finalLines = new List<BomLine>();
        foreach (var edit in edits)
        {
            if (edit.LineId is { } lineId)
            {
                var line = _lines.FirstOrDefault(l => l.Id == lineId) ?? throw new BomLineNotFoundException(Id, lineId);
                if (line.TargetKind != edit.Spec.TargetKind || line.TargetPartId != edit.Spec.TargetPartId
                    || line.TargetAssemblyId != edit.Spec.TargetAssemblyId || line.TargetNonPrintedComponentId != edit.Spec.TargetNonPrintedComponentId)
                {
                    throw new ArgumentException("An existing BOM line's target cannot change; remove it and add a new line.", nameof(edits));
                }

                BomLine.ValidateQuantity(line.TargetKind, edit.Spec.Quantity);
                EnsureNoDuplicateTarget(finalLines, line);
                finalLines.Add(line);
                kept.Add((line, edit.Spec.Quantity));
            }
            else
            {
                var line = BomLine.Create(Id, edit.Spec);
                EnsureNoDuplicateTarget(finalLines, line);
                finalLines.Add(line);
                added.Add(line);
            }
        }

        foreach (var (line, quantity) in kept)
        {
            line.UpdateQuantity(quantity);
        }

        _lines.RemoveAll(l => !finalLines.Contains(l));
        _lines.AddRange(added);
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return added;
    }

    private static void EnsureNoDuplicateTarget(List<BomLine> lines, BomLine candidate)
    {
        var isDuplicate = candidate.TargetKind switch
        {
            BomLineTargetKind.Part => lines.Any(l => l.TargetKind == BomLineTargetKind.Part && l.TargetPartId == candidate.TargetPartId),
            BomLineTargetKind.Assembly => lines.Any(l => l.TargetKind == BomLineTargetKind.Assembly && l.TargetAssemblyId == candidate.TargetAssemblyId),
            BomLineTargetKind.NonPrintedComponent => lines.Any(l => l.TargetKind == BomLineTargetKind.NonPrintedComponent && l.TargetNonPrintedComponentId == candidate.TargetNonPrintedComponentId),
            _ => false,
        };

        if (isDuplicate)
        {
            var targetId = candidate.TargetPartId ?? candidate.TargetAssemblyId ?? candidate.TargetNonPrintedComponentId!.Value;
            throw new DuplicateBomLineTargetException(candidate.TargetKind, targetId);
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Assembly name is required.", nameof(name));
        }
    }
}
