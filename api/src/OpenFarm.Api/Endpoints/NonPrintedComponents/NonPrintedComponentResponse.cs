using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Api.Endpoints.NonPrintedComponents;

public sealed class NonPrintedComponentResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal? UnitCost { get; set; }

    public static NonPrintedComponentResponse FromDomain(NonPrintedComponent component) => new()
    {
        Id = component.Id,
        Name = component.Name,
        UnitCost = component.UnitCost,
    };

    public static NonPrintedComponentResponse FromSummary(NonPrintedComponentSummary summary) => new()
    {
        Id = summary.Id,
        Name = summary.Name,
        UnitCost = summary.UnitCost,
    };
}
