namespace OpenFarm.Application.Assemblies;

/// <param name="Search">Whitespace-separated terms; each must match the assembly's name.</param>
public sealed record GetAssembliesQuery(int Page, int PageSize, OpenFarm.Application.Common.ListSort? Sorting = null, string? Folder = null, string? Search = null);

public sealed record AssemblySummary(Guid Id, string Name, string? Description, int LineCount, Guid? FolderId = null);

public sealed record AssembliesPage(IReadOnlyList<AssemblySummary> Assemblies, int TotalCount);
