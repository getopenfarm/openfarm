namespace OpenFarm.Application.Assemblies;

public sealed record ListNonPrintedComponentsQuery(int Page, int PageSize, OpenFarm.Application.Common.ListSort? Sorting = null);

public sealed record NonPrintedComponentSummary(Guid Id, string Name, decimal? UnitCost);

public sealed record NonPrintedComponentsPage(IReadOnlyList<NonPrintedComponentSummary> Components, int TotalCount);
