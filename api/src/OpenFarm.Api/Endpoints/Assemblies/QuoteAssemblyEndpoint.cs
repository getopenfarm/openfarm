using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class QuoteLineRequest
{
    /// <summary><c>Part</c>, <c>Assembly</c> or <c>NonPrintedComponent</c>.</summary>
    public string TargetKind { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class QuotePricingRequest
{
    /// <summary><c>Price</c>, <c>Margin</c> or <c>Markup</c>.</summary>
    public string Mode { get; set; } = "Price";
    public decimal Value { get; set; }
}

/// <summary>Send <c>lines</c> (and <c>additionalCosts</c>) to quote an unsaved draft, or only <c>assemblyId</c> to quote a saved assembly.</summary>
public sealed class QuoteAssemblyRequest
{
    public Guid? AssemblyId { get; set; }
    public List<QuoteLineRequest>? Lines { get; set; }
    public List<AssemblyCostInput>? AdditionalCosts { get; set; }
    /// <summary>The pricing rule to price with; omit to use none for a draft, or the saved rule for a saved assembly.</summary>
    public QuotePricingRequest? Pricing { get; set; }
}

public sealed class QuoteAssemblyValidator : Validator<QuoteAssemblyRequest>
{
    public QuoteAssemblyValidator()
    {
        RuleFor(r => r).Must(r => r.Lines is not null || r.AssemblyId is not null).WithMessage("Send lines or assemblyId.");
        RuleFor(r => r.Lines).Must(l => l is null || l.Count <= 500).WithMessage("A draft quotes at most 500 lines.");
        RuleForEach(r => r.Lines).ChildRules(line =>
            line.RuleFor(l => l.TargetKind).Must(k => Enum.TryParse<BomLineTargetKind>(k, out _))
                .WithMessage("targetKind must be one of Part, Assembly, NonPrintedComponent."));
        RuleFor(r => r.Pricing!.Mode).Must(m => AssemblyPricing.Modes.Contains(m)).When(r => r.Pricing is not null)
            .WithMessage("mode must be Price, Margin or Markup.");
    }
}

/// <summary>
/// POST /api/v1/assemblies/quote — the Build panel's cost totals and the Pricing table per fee profile (dev-notes/PRICING.md).
/// Read-only; without settings:fees:read the table holds only the "No fees" row.
/// </summary>
[Permission("assemblies:view")]
public sealed class QuoteAssemblyEndpoint(AssemblyQuoteService quotes) : Endpoint<QuoteAssemblyRequest, AssemblyQuote>
{
    public override void Configure() { Post("/api/v1/assemblies/quote"); Policies("Permission"); }

    public override async Task HandleAsync(QuoteAssemblyRequest req, CancellationToken ct)
    {
        var fees = (await CredentialAccess.EffectiveAsync(HttpContext, browser: true)).Contains("settings:fees:read");
        var pricing = req.Pricing is null ? null : new QuotePricing(req.Pricing.Mode, req.Pricing.Value);
        Response = req.Lines is { } lines
            ? await quotes.QuoteAsync([.. lines.Select(l => new QuoteLine(Enum.Parse<BomLineTargetKind>(l.TargetKind), l.TargetId, l.Quantity))],
                req.AdditionalCosts ?? [], pricing, fees, ct)
            : await quotes.QuoteSavedAsync(req.AssemblyId!.Value, pricing, fees, ct);
    }
}
