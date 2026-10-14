namespace OpenFarm.Application.Assemblies;

public sealed record GetAssembliesQuery(int Page, int PageSize, OpenFarm.Application.Common.ListSort? Sorting = null);

public sealed record AssemblySummary(Guid Id, string Name, string? Description, int LineCount);

public sealed record AssembliesPage(IReadOnlyList<AssemblySummary> Assemblies, int TotalCount);
