using OpenFarm.Api.Processors;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class BomLineResponse : IHasEntityIds
{
    public Guid Id { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    // Internal to the post-processor extension only — not part of the public response contract,
    // mirrors AddFeedUnitResponse's precedent. BomLineAdded/BomLineQuantityChanged (FR-015) must log
    // against the owning Assembly, not this line's own Id above, so the endpoints set this after
    // construction; ActivityLogPostProcessor prefers IHasEntityIds over its default Id-reflection path.
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid AssemblyId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> EntityIds => [AssemblyId.ToString()];

    public static BomLineResponse FromDetail(BomLineDetail line) => new()
    {
        Id = line.Id,
        TargetKind = line.TargetKind.ToString(),
        TargetId = line.TargetId,
        TargetName = line.TargetName,
        Quantity = line.Quantity,
    };

    public static BomLineResponse FromDomain(BomLine line, string targetName) => new()
    {
        Id = line.Id,
        TargetKind = line.TargetKind.ToString(),
        TargetId = line.TargetPartId ?? line.TargetAssemblyId ?? line.TargetNonPrintedComponentId ?? Guid.Empty,
        TargetName = targetName,
        Quantity = line.Quantity,
    };
}

public sealed class AssemblyResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<BomLineResponse> Lines { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static AssemblyResponse FromDetail(AssemblyDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        Description = detail.Description,
        Lines = detail.Lines.Select(BomLineResponse.FromDetail).ToList(),
        CreatedAtUtc = detail.CreatedAtUtc,
        UpdatedAtUtc = detail.UpdatedAtUtc,
    };
}

public sealed class AssemblySummaryResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int LineCount { get; set; }

    public static AssemblySummaryResponse FromSummary(AssemblySummary summary) => new()
    {
        Id = summary.Id,
        Name = summary.Name,
        Description = summary.Description,
        LineCount = summary.LineCount,
    };
}
