namespace OpenFarm.Application.Assemblies;

public sealed record ListNonPrintedComponentsQuery(int Page, int PageSize);

public sealed record NonPrintedComponentSummary(Guid Id, string Name, string Unit, decimal? UnitCost);

public sealed record NonPrintedComponentsPage(IReadOnlyList<NonPrintedComponentSummary> Components, int TotalCount);
