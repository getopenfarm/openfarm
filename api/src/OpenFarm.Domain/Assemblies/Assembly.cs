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

    private Assembly(Guid id, Guid organizationId, string name, string? description, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Description = description;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public IReadOnlyList<BomLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>FR-002: requires at least one line. Cycle/depth validation is the caller's job (AssemblyGraph, data-model.md) — this factory has no access to the org-wide edge graph.</summary>
    public static Assembly Create(Guid organizationId, string name, string? description, IReadOnlyList<BomLineSpec> lines)
    {
        ValidateName(name);

        if (lines.Count == 0)
        {
            throw new AssemblyMustHaveAtLeastOneLineException();
        }

        var assembly = new Assembly(Guid.NewGuid(), organizationId, name, description, DateTimeOffset.UtcNow);

        foreach (var spec in lines)
        {
            var line = BomLine.Create(organizationId, assembly.Id, spec);
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

    /// <summary>FR-013 duplicate-target check happens here. Cycle/depth validation (Assembly-kind targets) is the caller's job, run before this against the org-wide edge graph.</summary>
    public BomLine AddLine(BomLineSpec spec)
    {
        var line = BomLine.Create(OrganizationId, Id, spec);
        EnsureNoDuplicateTarget(_lines, line);

        _lines.Add(line);
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return line;
    }

    public void UpdateLineQuantity(Guid lineId, decimal quantity)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId) ?? throw new BomLineNotFoundException(Id, lineId);

        line.UpdateQuantity(quantity);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Rejects if it would leave zero lines (FR-002) — delete the assembly itself instead.</summary>
    public void RemoveLine(Guid lineId)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId) ?? throw new BomLineNotFoundException(Id, lineId);

        if (_lines.Count == 1)
        {
            throw new AssemblyMustHaveAtLeastOneLineException();
        }

        _lines.Remove(line);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
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
