using FluentValidation;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Api.Endpoints.CostCatalog;

public sealed record CostItemResponse(Guid Id, string Name, string? Icon, CatalogPricing Pricing, decimal? EffectiveRate)
{
    public static CostItemResponse FromDomain(NonPrintedComponent item) => new(item.Id, item.Name, item.Icon, item.Pricing, item.UnitCost);
}

public sealed class SaveCostItemRequest : OpenFarm.Api.Processors.IActivityLoggable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public CatalogPricing Pricing { get; set; } = new();
    public string EntityType => "NonPrintedComponent";
    public string EventType => Id == Guid.Empty ? "NonPrintedComponentCreated" : "NonPrintedComponentEdited";
}

public sealed class SaveCostItemRequestValidator : FluentValidation.AbstractValidator<SaveCostItemRequest>
{
    public SaveCostItemRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Icon).MaximumLength(40).Matches("^[a-z0-9]+(-[a-z0-9]+)*$").When(x => !string.IsNullOrEmpty(x.Icon));
        RuleFor(x => x.Pricing).NotNull();
    }
}
