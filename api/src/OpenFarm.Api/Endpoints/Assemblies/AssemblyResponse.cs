using FluentValidation;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class BomLineResponse
{
    public Guid Id { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public static BomLineResponse FromDetail(BomLineDetail line) => new()
    {
        Id = line.Id,
        TargetKind = line.TargetKind.ToString(),
        TargetId = line.TargetId,
        TargetName = line.TargetName,
        Quantity = line.Quantity,
    };
}

/// <summary>Selling price rule: <c>Price</c> (fixed amount), <c>Margin</c> (% of price kept after channel fees) or <c>Markup</c> (% on cost).</summary>
public sealed class AssemblyPricingDto
{
    public string Mode { get; set; } = "Price";

    public decimal Value { get; set; }

    public AssemblyPricing ToDomain() => new(Mode, Value);

    public static AssemblyPricingDto? From(AssemblyPricing? pricing) =>
        pricing is null ? null : new() { Mode = pricing.Mode, Value = pricing.Value };
}

public sealed class AssemblyPricingDtoValidator : AbstractValidator<AssemblyPricingDto>
{
    public AssemblyPricingDtoValidator()
    {
        RuleFor(x => x.Mode).Must(m => AssemblyPricing.Modes.Contains(m))
            .WithMessage("mode must be one of Price, Margin, Markup.");
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.Value).LessThan(100m).When(x => x.Mode == "Margin").WithMessage("Margin must be below 100%.");
    }
}

public sealed class AssemblyResponse
{
    public Guid? FolderId { get; set; }
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<BomLineResponse> Lines { get; set; } = [];

    public List<OpenFarm.Domain.Assemblies.AssemblyCostInput> AdditionalCosts { get; set; } = [];

    public AssemblyPricingDto? Pricing { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static AssemblyResponse FromDetail(AssemblyDetail detail) => new()
    {
        Id = detail.Id,
        FolderId = detail.FolderId,
        Name = detail.Name,
        Description = detail.Description,
        Lines = detail.Lines.Select(BomLineResponse.FromDetail).ToList(),
        AdditionalCosts = detail.AdditionalCosts?.ToList() ?? [],
        Pricing = AssemblyPricingDto.From(detail.Pricing),
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

    public IReadOnlyList<string> PreviewThumbnails { get; set; } = [];

    public static AssemblySummaryResponse FromSummary(AssemblySummary summary) => new()
    {
        Id = summary.Id,
        Name = summary.Name,
        Description = summary.Description,
        LineCount = summary.LineCount,
    };
}
