namespace OpenFarm.Application.Assemblies;

public sealed record GetAssembliesQuery(int Page, int PageSize);

public sealed record AssemblySummary(Guid Id, string Name, string? Description, int LineCount);

public sealed record AssembliesPage(IReadOnlyList<AssemblySummary> Assemblies, int TotalCount);
