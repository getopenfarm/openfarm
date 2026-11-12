using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Application.Production;

public sealed record GetFilamentShortagesQuery;

/// <summary>A colour that queued finite work needs more of than its spools hold.</summary>
public sealed record FilamentShortage(Guid FilamentVariantId, decimal QueuedGrams, decimal OnHandGrams);

/// <summary>A material and colour as job matching compares them: material exactly, hex ignoring case.</summary>
public readonly record struct ColourKey(string MaterialType, string ColorHex)
{
    public static ColourKey Of(string materialType, string colorHex) => new(materialType, colorHex.ToUpperInvariant());
}

/// <summary>
/// Grams unfinished finite jobs still need. A line tied to a catalog colour counts for that colour only; any other line
/// counts once for its material and colour, which a spool of any brand with that material and hex can print.
/// </summary>
public sealed record QueuedDemand(IReadOnlyDictionary<Guid, decimal> ByVariant, IReadOnlyDictionary<ColourKey, decimal> ByColour)
{
    public static readonly QueuedDemand None = new(new Dictionary<Guid, decimal>(), new Dictionary<ColourKey, decimal>());

    /// <summary>This demand plus more grams, tied to <paramref name="variantId"/> or else shared by <paramref name="colour"/>.</summary>
    public QueuedDemand Plus(Guid? variantId, ColourKey? colour, decimal grams)
    {
        var byVariant = new Dictionary<Guid, decimal>(ByVariant);
        var byColour = new Dictionary<ColourKey, decimal>(ByColour);
        if (variantId is { } id) byVariant[id] = byVariant.GetValueOrDefault(id) + grams;
        else if (colour is { } key) byColour[key] = byColour.GetValueOrDefault(key) + grams;
        return new(byVariant, byColour);
    }
}

/// <summary>
/// What finite jobs still to print (queued, printing or paused: nothing is deducted until a print ends) take of each colour,
/// against its grams on hand. A line narrowed to a colour counts against it; otherwise against every colour of the same
/// material and hex, so a shortage may be over-reported but never missed. A line with no known grams, unlimited work,
/// "any material" lines and any-colour lines (of one filament, or of the material) are left out.
/// </summary>
public sealed class GetFilamentShortagesQueryHandler(
    IPrintJobRepository jobs, IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>> stockQuery)
    : IQueryHandler<GetFilamentShortagesQuery, IReadOnlyList<FilamentShortage>>
{
    public async Task<IReadOnlyList<FilamentShortage>> HandleAsync(GetFilamentShortagesQuery query, CancellationToken cancellationToken)
    {
        var stock = await stockQuery.HandleAsync(new GetFilamentStockQuery(), cancellationToken);
        var queued = await QueuedGramsAsync(jobs, stock, cancellationToken);
        return stock.Where(s => queued.GetValueOrDefault(s.FilamentVariantId) > s.Stock.OnHandGrams)
            .Select(s => new FilamentShortage(s.FilamentVariantId, queued[s.FilamentVariantId], s.Stock.OnHandGrams))
            .ToList();
    }

    /// <summary>Grams that unfinished finite jobs still need, by colour, under the rules above: shared grams count for every matching colour.</summary>
    public static async Task<Dictionary<Guid, decimal>> QueuedGramsAsync(IPrintJobRepository jobs, IReadOnlyList<VariantStock> stock, CancellationToken cancellationToken)
    {
        var demand = await QueuedDemandAsync(jobs, cancellationToken);
        var queued = new Dictionary<Guid, decimal>();
        foreach (var s in stock)
        {
            var grams = demand.ByVariant.GetValueOrDefault(s.FilamentVariantId) + demand.ByColour.GetValueOrDefault(ColourKey.Of(s.MaterialType, s.ColorHex));
            if (grams > 0) queued[s.FilamentVariantId] = grams;
        }
        return queued;
    }

    /// <summary>Grams unfinished finite jobs still need, tied colours apart from shared material-and-colour work.</summary>
    public static async Task<QueuedDemand> QueuedDemandAsync(IPrintJobRepository jobs, CancellationToken cancellationToken)
    {
        var byVariant = new Dictionary<Guid, decimal>();
        var byColour = new Dictionary<ColourKey, decimal>();
        foreach (var job in await jobs.ListUnfinishedFiniteAsync(cancellationToken))
        {
            if (job.GetProductionPart() is not { } part) continue;
            var requirements = job.Filaments.Select(line => line.Requirement).ToList();
            foreach (var requirement in requirements)
            {
                if (ProductionJobRequirements.EstimatedGrams(part, requirement, requirements.Count, job, job.FileRevisionHash) is not { } grams) continue;
                // ponytail: any-colour lines are left out like "any material"; pool them per filament/material if farms rely on them.
                if (requirement.NarrowFilamentId is not null || requirement.AnyColor) continue;
                if (requirement.NarrowFilamentVariantId is { } id) byVariant[id] = byVariant.GetValueOrDefault(id) + grams;
                else if (requirement.MaterialType is { } material && requirement.DetectedColorHex is { } hex)
                    byColour[ColourKey.Of(material, hex)] = byColour.GetValueOrDefault(ColourKey.Of(material, hex)) + grams;
            }
        }
        return new QueuedDemand(byVariant, byColour);
    }

    /// <summary>The colour a line is narrowed to; otherwise every colour of the same material and hex.</summary>
    public static IEnumerable<VariantStock> Matching(IReadOnlyList<VariantStock> stock, Guid? variantId, string? materialType, string? colorHex) =>
        variantId is { } id
            ? stock.Where(s => s.FilamentVariantId == id)
            : stock.Where(s => materialType is not null && colorHex is not null
                && s.MaterialType == materialType
                && string.Equals(s.ColorHex, colorHex, StringComparison.OrdinalIgnoreCase));
}
