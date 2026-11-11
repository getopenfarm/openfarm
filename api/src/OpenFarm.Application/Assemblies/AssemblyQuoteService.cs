using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Assemblies;

public sealed record QuoteLine(BomLineTargetKind TargetKind, Guid TargetId, decimal Quantity);

/// <param name="Mode">Price, Margin or Markup; a draft value is priced as typed.</param>
public sealed record QuotePricing(string Mode, decimal Value);

/// <summary>A cost subtotal and how many of its contributions were priced or unknown.</summary>
public sealed record QuoteMetric(decimal Total, int Priced, int Missing);

/// <summary>One filament colour of the print estimate. <see cref="Name"/> and <see cref="SwatchHex"/> are what the Build panel shows.</summary>
public sealed record QuoteColor(string? MaterialType, string? ColorHex, Guid? VariantId, decimal? Grams, int Missing,
    string Name, string? SwatchHex, decimal Cost, int CostPriced, int CostMissing);

public sealed record QuoteFilament(decimal Total, int Priced, int MissingSelection, int MissingPrice, int MissingGrams)
{
    public bool Incomplete => MissingSelection + MissingPrice + MissingGrams > 0;
}

public sealed record QuoteCategory(string Category, decimal Amount);

public sealed record QuoteTotal(decimal Amount, bool Partial, bool Available);

/// <param name="ProfileId">Null for the "No fees" row shown while no fee profile exists.</param>
public sealed record QuoteProfile(Guid? ProfileId, string Name, IReadOnlyList<Fee> Fees, decimal FeePercent, decimal FixedFee, ProfilePricing Pricing);

/// <summary>
/// What one finished product costs and sells for on each fee profile (dev-notes/PRICING.md). <see cref="Total"/> includes shipping;
/// product cost is <c>Total.Amount - Shipping</c>. <see cref="Profiles"/> holds one row per fee profile, or only the "No fees" row
/// while there is none or the caller may not read fees.
/// </summary>
public sealed record AssemblyQuote(int Parts, int Components, PrintEstimate Print, IReadOnlyList<QuoteColor> Colors, QuoteFilament Filament,
    QuoteMetric Depreciation, QuoteMetric Electricity, decimal FailurePercent, decimal Failure, bool FailureAvailable, bool PrintPartial,
    IReadOnlyList<QuoteCategory> Breakdown, bool CostsPartial, decimal Shipping, QuoteTotal Total, IReadOnlyList<string> Notes,
    IReadOnlyList<QuoteProfile> Profiles);

/// <summary>
/// The Build panel's totals and the Pricing table, for a saved assembly or an unsaved draft. Direct lines roll up as they are;
/// each sub-assembly is exploded at quantity 1 and scaled by its line quantity, inheriting its production costs.
/// Filament is priced at the colour's (else the filament's) price per kilogram, never a spool's.
/// </summary>
public sealed class AssemblyQuoteService(
    IAssemblyRepository assemblies,
    INonPrintedComponentRepository items,
    IPartRepository parts,
    IQueryHandler<ExplodeAssemblyQuery, BomExplosionResult> explode,
    AssemblyMachineCosts machineCosts,
    IQueryHandler<GetFilamentsQuery, IReadOnlyList<FilamentSummary>> filaments,
    IFarmSettingsRepository settings,
    IFeeProfileRepository feeProfiles)
{
    public async Task<AssemblyQuote> QuoteSavedAsync(Guid assemblyId, QuotePricing? pricing, bool includeProfiles, CancellationToken ct)
    {
        var assembly = await assemblies.GetByIdAsync(assemblyId, ct) ?? throw new AssemblyNotFoundException(assemblyId);
        var lines = assembly.Lines.Select(l => new QuoteLine(l.TargetKind, l.TargetKind switch
        {
            BomLineTargetKind.Part => l.TargetPartId!.Value,
            BomLineTargetKind.Assembly => l.TargetAssemblyId!.Value,
            _ => l.TargetNonPrintedComponentId!.Value,
        }, l.Quantity)).ToList();
        var saved = assembly.Pricing is { } p ? new QuotePricing(p.Mode, p.Value) : null;
        return await QuoteAsync(lines, [.. assembly.AdditionalCosts.Select(c => c.Input)], pricing ?? saved, includeProfiles, ct);
    }

    public async Task<AssemblyQuote> QuoteAsync(IReadOnlyList<QuoteLine> lines, IReadOnlyList<AssemblyCostInput> additionalCosts,
        QuotePricing? pricing, bool includeProfiles, CancellationToken ct)
    {
        // Roll up: mid-typing quantities (0, negative) contribute nothing.
        var positive = lines.Where(l => l.Quantity > 0).ToList();
        var subs = new Dictionary<Guid, BomExplosionResult>();
        foreach (var id in positive.Where(l => l.TargetKind == BomLineTargetKind.Assembly).Select(l => l.TargetId).Distinct())
            subs[id] = await explode.HandleAsync(new ExplodeAssemblyQuery(id, 1), ct);
        var partQty = new Dictionary<Guid, decimal>();
        var componentQty = new Dictionary<Guid, decimal>();
        static void Add(Dictionary<Guid, decimal> map, Guid id, decimal qty) => map[id] = map.GetValueOrDefault(id) + qty;
        foreach (var line in positive)
        {
            if (line.TargetKind == BomLineTargetKind.Part) Add(partQty, line.TargetId, line.Quantity);
            else if (line.TargetKind == BomLineTargetKind.NonPrintedComponent) Add(componentQty, line.TargetId, line.Quantity);
            else
            {
                foreach (var p in subs[line.TargetId].PrintedParts) Add(partQty, p.PartId, p.Quantity * line.Quantity);
                foreach (var c in subs[line.TargetId].NonPrintedComponents) Add(componentQty, c.NonPrintedComponentId, c.Quantity * line.Quantity);
            }
        }

        // Print estimate, as POST /api/v1/parts/print-estimate.
        var inputs = partQty.OrderBy(p => p.Key).ToList();
        var catalog = (await parts.GetForEstimatesAsync([.. inputs.Select(p => p.Key)], ct)).ToDictionary(p => p.Id);
        var print = PrintEstimates.Sum(inputs.Select(i => PrintEstimates.ForPart(catalog.GetValueOrDefault(i.Key), i.Value)));

        // Printer depreciation and electricity per part.
        var machine = await machineCosts.CalculateAsync([.. inputs.Select(i => new ExplodedPartQuantity(i.Key, "", i.Value))], ct);
        var depreciation = Metric(machine.Select(m => m.PrinterDepreciation));
        var electricity = Metric(machine.Select(m => m.Electricity));

        // Hardware: physical cost items on BOM lines; services cannot be components.
        var byId = new Dictionary<Guid, NonPrintedComponent?>();
        async Task<NonPrintedComponent?> ItemAsync(Guid id) =>
            byId.TryGetValue(id, out var item) ? item : byId[id] = await items.GetByIdAsync(id, ct);
        var breakdown = new Dictionary<string, decimal>();
        int missingCost = 0, missingAdditional = 0, pricedAdditional = 0;
        foreach (var (id, qty) in componentQty)
        {
            var item = await ItemAsync(id);
            if (item is null || item.ItemType == "Service" || item.UnitCost is not { } unitCost) { missingCost++; continue; }
            breakdown[item.Category] = breakdown.GetValueOrDefault(item.Category) + (qty * unitCost);
        }
        void AddCost(string category, decimal? amount)
        {
            if (amount is not { } value) { missingAdditional++; return; }
            pricedAdditional++;
            breakdown[category] = breakdown.GetValueOrDefault(category) + value;
        }
        foreach (var line in additionalCosts)
        {
            var item = await ItemAsync(line.CatalogItemId);
            AddCost(item?.Category ?? "Other", item?.UnitCost is { } rate && Usage(line, item) is > 0 and var used ? used * rate : null);
        }
        foreach (var line in positive.Where(l => l.TargetKind == BomLineTargetKind.Assembly))
            foreach (var inherited in subs[line.TargetId].ProductionCosts ?? [])
                AddCost(inherited.Category ?? "Other", inherited.Cost * line.Quantity);
        var shipping = breakdown.GetValueOrDefault("Shipping");
        var costsPartial = missingCost > 0 || missingAdditional > 0;

        // Filament at the colour's price per kilogram, else the filament's.
        var variants = (await filaments.HandleAsync(new GetFilamentsQuery(), ct))
            .SelectMany(f => f.Variants.Select(v => (v.Id, Price: v.PricePerKg ?? f.PricePerKg, v.ColorHex,
                Name: Join(" · ", Join(" ", f.VendorName, f.Material), v.ColorName))))
            .ToDictionary(v => v.Id);
        decimal filamentTotal = 0;
        int filamentPriced = 0, missingSelection = 0, missingPrice = 0, missingGrams = 0;
        var colors = new List<QuoteColor>();
        foreach (var c in print.Colors)
        {
            var known = c.VariantId is { } vid && variants.TryGetValue(vid, out var v) ? v : default;
            var price = known.Id == default ? null : known.Price;
            if (c.Grams is null || c.Missing > 0) missingGrams++;
            if (c.VariantId is null) missingSelection++;
            else if (price is null) missingPrice++;
            decimal cost = 0;
            var priced = 0;
            if (c.Grams is { } grams && price is { } perKg)
            {
                cost = grams / 1000 * perKg;
                filamentTotal += cost;
                filamentPriced++;
                priced = 1;
            }
            var name = known.Id != default ? (known.Name.Length > 0 ? known.Name : "Filament unavailable")
                : c.VariantId is not null ? "Filament unavailable"
                : c.ColorHex is not null ? $"{c.MaterialType ?? "Any"} {c.ColorHex}"
                : "Unknown color";
            colors.Add(new QuoteColor(c.MaterialType, c.ColorHex, c.VariantId, c.Grams, c.Missing, name,
                known.Id != default ? known.ColorHex : c.ColorHex, cost, priced, c.Grams is null || price is null || c.Missing > 0 ? 1 : 0));
        }
        var filament = new QuoteFilament(filamentTotal, filamentPriced, missingSelection, missingPrice, missingGrams);

        // Failed prints waste filament and machine time, never packaging or shipping.
        var failurePercent = (await settings.GetAsync(ct)).FailureAllowancePercent;
        var machineTotal = depreciation.Total + electricity.Total;
        var failure = failurePercent / 100 * (filament.Total + machineTotal);
        var machinePartial = depreciation.Missing + electricity.Missing > 0;
        var printPartial = filament.Incomplete || machinePartial;
        var failureAvailable = partQty.Count == 0 || filament.Priced > 0 || depreciation.Priced > 0 || electricity.Priced > 0;
        var total = new QuoteTotal(filament.Total + breakdown.Values.Sum() + machineTotal + failure, printPartial || costsPartial,
            depreciation.Priced > 0 || electricity.Priced > 0 || filament.Priced > 0 || pricedAdditional > 0
            || componentQty.Count > missingCost || (partQty.Count == 0 && componentQty.Count == 0));

        var notes = new List<string>();
        if (depreciation.Missing > 0)
            notes.Add("Printer depreciation incomplete. Configure model costs in Settings → Printer costs; parts also need a known estimation model and print time.");
        if (electricity.Missing > 0)
            notes.Add("Electricity incomplete. Configure average printing power and electricity price in Settings → General (model power is in Settings → Printer costs); parts also need a known estimation model and print time.");
        if (missingSelection > 0) notes.Add("Some filament weight has no selected catalog variant or color attribution.");
        if (missingPrice > 0) notes.Add("Selected filament has no price. Filament cost incomplete.");
        if (missingGrams > 0) notes.Add("Filament weight estimates unavailable. Filament cost incomplete.");
        if (print.Time.Missing > 0) notes.Add("Some print time estimates are unavailable.");
        if (print.Filament.Missing > 0) notes.Add("Some filament weight estimates are unavailable.");
        if (print.Approximate) notes.Add("Partial-plate estimates are proportional approximations; startup, purge, and travel may not scale linearly.");
        if (missingAdditional > 0) notes.Add("Cost items have missing prices or usage.");
        if (missingCost > 0) notes.Add($"{missingCost} {(missingCost == 1 ? "component" : "components")} without cost.");

        // A fixed price needs no cost; margin and markup do.
        var productCost = total.Available ? total.Amount : 0;
        var priceable = total.Available || pricing?.Mode == "Price";
        ProfilePricing Price(decimal percent, decimal fixedFee) =>
            FeeProfilePricing.Calculate(productCost, percent, fixedFee, priceable ? pricing?.Mode : null, pricing?.Value ?? 0);
        // Without fee access the caller sees only the "No fees" row, which tells nothing about saved profiles.
        var saved = includeProfiles ? await feeProfiles.ListAsync(ct) : [];
        List<QuoteProfile> profiles = saved.Count == 0
            ? [new QuoteProfile(null, "No fees", [], 0, 0, Price(0, 0))]
            : [.. saved.Select(p => new QuoteProfile(p.Id, p.Name, p.Fees, p.FeePercent, p.FixedFee, Price(p.FeePercent, p.FixedFee)))];

        return new AssemblyQuote(partQty.Count, componentQty.Count, print, colors, filament, depreciation, electricity, failurePercent, failure,
            failureAvailable, printPartial, [.. breakdown.Select(b => new QuoteCategory(b.Key, b.Value))], costsPartial, shipping, total, notes, profiles);
    }

    private static QuoteMetric Metric(IEnumerable<decimal?> values)
    {
        var rows = values.ToList();
        return new(rows.Sum(v => v ?? 0), rows.Count(v => v is not null), rows.Count(v => v is null));
    }

    /// <summary>A draft line's usage, or null when it is not valid yet (a missing usage, as the editor shows it).</summary>
    private static decimal? Usage(AssemblyCostInput line, NonPrintedComponent item)
    {
        try { return line.Usage(item); }
        catch (ArgumentException) { return null; }
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrEmpty(p)));
}
