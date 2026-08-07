using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed record GetAssemblyQuery(Guid AssemblyId);

public sealed record BomLineDetail(Guid Id, BomLineTargetKind TargetKind, Guid TargetId, string TargetName, decimal Quantity);

public sealed record AssemblyDetail(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<BomLineDetail> Lines,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
