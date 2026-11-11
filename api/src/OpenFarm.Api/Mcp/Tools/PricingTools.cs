using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="PercentFees">Percent fees summed: taken off the sale price.</param>
/// <param name="FixedFees">Fixed fees summed: taken once per order, in the farm currency.</param>
public sealed record McpFeeProfile(Guid ProfileId, string Name, IReadOnlyList<FeeDto> Fees, decimal PercentFees, decimal FixedFees);

public sealed record McpFeeProfiles(IReadOnlyList<McpFeeProfile> Profiles);

public sealed record McpFee(
    [property: Description("What the fee is, such as Commission or Payment")] string Name,
    [property: Description("Percent (of the sale price) or Fixed (per order, in the farm currency)")] string Type,
    [property: Description("Percent 0 to below 100, or a fixed amount, 0 or more")] decimal Amount);

public sealed record McpCostLine(string Name, decimal Amount);

/// <param name="Price">Null without a pricing rule, or when fees make the margin unreachable.</param>
/// <param name="MarginPercent">Profit after fees as a percent of the price.</param>
/// <param name="MarkupPercent">Price over cost, before fees.</param>
/// <param name="MaxDiscountPercent">Largest discount before a loss; 0 or less means none.</param>
public sealed record McpProfileQuote(string Profile, decimal? Price, decimal? Fees, decimal? Profit, decimal? MarginPercent, decimal? MarkupPercent,
    decimal BreakEven, decimal? MaxDiscountPercent, bool MarginUnreachable);

/// <param name="ProductCost">What one unit costs to make; TotalCost adds the shipping allowance and is what prices work from.</param>
/// <param name="CostPartial">Some cost is unknown, so profit may be lower than shown; Notes say what.</param>
/// <param name="Pricing">The pricing rule priced with: the one passed, else the saved one.</param>
public sealed record McpQuote(Guid AssemblyId, string Assembly, decimal ProductCost, decimal Shipping, decimal TotalCost, bool CostPartial,
    bool CostAvailable, IReadOnlyList<McpCostLine> Costs, string? Pricing, IReadOnlyList<McpProfileQuote> Profiles, IReadOnlyList<string> Notes);

public static class PricingTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_fee_profiles", "List fee profiles",
            "Fee profiles: one per place products sell, such as Shopee or Direct, with their percent and fixed fees. "
            + "Every assembly is priced against every profile.",
            async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
                new McpFeeProfiles((await request.Service<IFeeProfileRepository>().ListAsync(ct)).Select(Profile).ToList()),
            "settings:fees:read"),
        McpTools.Write("save_fee_profile", "Save fee profile",
            "Creates a fee profile when profileId is omitted, or replaces one profile's name and full fee list. Percent fees must total below 100. "
            + "A monthly fee is a fixed fee per order: the monthly amount divided by orders per month. This changes every assembly's price on that profile.",
            destructive: true, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Profile name, unique, such as Shopee")] string name,
                [Description("Every fee of the profile; it replaces the saved list")] McpFee[] fees,
                [Description("Profile to change; omit to create one")] Guid? profileId = null,
                CancellationToken ct = default) =>
            {
                var dto = new SaveFeeProfileRequest
                {
                    Id = profileId ?? Guid.Empty, Name = name,
                    Fees = [.. (fees ?? []).Select(f => new FeeDto
                    {
                        Name = f.Name,
                        Type = Fee.Types.FirstOrDefault(t => t.Equals(f.Type, StringComparison.OrdinalIgnoreCase)) ?? f.Type,
                        Amount = f.Amount,
                    })],
                };
                var saved = await request.WriteAsync(dto, async () =>
                {
                    var result = await request.Service<IFeeProfileRepository>().SaveAsync(profileId, dto.Name,
                        dto.Fees.Select(f => new Fee(f.Name, f.Type, f.Amount)).ToList(), ct);
                    if (result.DuplicateName) throw new ArgumentException($"A fee profile named {dto.Name} already exists. Use list_fee_profiles and pass its profileId to change it.");
                    return FeeProfileDto.From(McpTools.Found(result.Profile, "Fee profile", "Use list_fee_profiles to find profile ids."));
                }, ct);
                return Profile((await request.Service<IFeeProfileRepository>().ListAsync(ct)).Single(p => p.Id == saved.Id));
            },
            "settings:fees:update"),
        McpTools.Read("quote_assembly", "Quote assembly",
            "What one assembly costs to make (filament, printer time, electricity, failure allowance, hardware, cost items, shipping) and, on each "
            + "fee profile, its price, fees, profit, margin, markup, break-even price and largest discount. Pass pricingMode and pricingValue "
            + "to try a rule without saving it; save one with save_assembly. Money is in the farm currency.",
            async (RequestContext<CallToolRequestParams> request,
                [Description("Assembly id from search_library")] Guid assemblyId,
                [Description("Price (a fixed amount), Margin (percent of price kept after fees) or Markup (percent on cost); omit for the saved rule")] string? pricingMode = null,
                [Description("The amount or percent for pricingMode")] decimal? pricingValue = null,
                CancellationToken ct = default) =>
            {
                var mode = pricingMode is null ? null
                    : AssemblyPricing.Modes.FirstOrDefault(m => m.Equals(pricingMode, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException("pricingMode must be Price, Margin or Markup.");
                if ((mode is null) != (pricingValue is null)) throw new ArgumentException("Pass pricingMode and pricingValue together.");
                var assembly = McpTools.Found(await request.Service<IQueryHandler<GetAssemblyQuery, AssemblyDetail?>>().HandleAsync(new GetAssemblyQuery(assemblyId), ct),
                    "Assembly", "Use search_library with library assemblies to find assembly ids.");
                var pricing = mode is null ? null : new QuotePricing(mode, pricingValue!.Value);
                var quote = await request.Service<AssemblyQuoteService>().QuoteSavedAsync(assemblyId, pricing, includeProfiles: true, ct);
                static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
                static decimal? Percent(decimal? fraction) => fraction is { } f ? Math.Round(f * 100, 1, MidpointRounding.AwayFromZero) : null;
                var rule = pricing ?? (assembly.Pricing is { } saved ? new QuotePricing(saved.Mode, saved.Value) : null);
                return new McpQuote(assembly.Id, assembly.Name, Money(quote.Total.Amount - quote.Shipping), Money(quote.Shipping), Money(quote.Total.Amount),
                    quote.Total.Partial, quote.Total.Available,
                    [new("Filament", Money(quote.Filament.Total)), new("Printer depreciation", Money(quote.Depreciation.Total)),
                        new("Electricity", Money(quote.Electricity.Total)), new($"Failure allowance ({quote.FailurePercent}%)", Money(quote.Failure)),
                        .. quote.Breakdown.Select(b => new McpCostLine(b.Category == "Labour" ? "Labor" : b.Category, Money(b.Amount)))],
                    rule is null ? null : rule.Mode == "Price" ? $"Price {rule.Value}" : $"{rule.Mode} {rule.Value}%",
                    quote.Profiles.Select(p => new McpProfileQuote(p.Name,
                        p.Pricing.Price is { } price ? Money(price) : null, p.Pricing.Fees is { } fees ? Money(fees) : null,
                        p.Pricing.Profit is { } profit ? Money(profit) : null, Percent(p.Pricing.Margin), Percent(p.Pricing.Markup),
                        Money(p.Pricing.BreakEven), Percent(p.Pricing.MaxDiscount), p.Pricing.Unreachable)).ToList(),
                    quote.Notes);
            },
            "assemblies:view", "settings:fees:read"),
    ];

    private static McpFeeProfile Profile(FeeProfile p) => new(p.Id, p.Name,
        p.Fees.Select(f => new FeeDto { Name = f.Name, Type = f.Type, Amount = f.Amount }).ToList(), p.FeePercent, p.FixedFee);
}
