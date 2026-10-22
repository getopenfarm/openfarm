using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Application.Production;

public sealed record GetFilamentShortagesQuery;

/// <summary>A colour that queued finite work needs more of than its spools hold.</summary>
public sealed record FilamentShortage(Guid FilamentVariantId, decimal QueuedGrams, decimal OnHandGrams);

/// <summary>
/// What finite jobs still to print (queued, printing or paused: nothing is deducted until a print ends) take of each colour,
/// against its grams on hand. A line narrowed to a colour counts against it; otherwise against every colour of the same
/// material and hex, so a shortage may be over-reported but never missed. A line with no known grams, unlimited work and
/// "any material" lines are left out.
/// </summary>
public sealed class GetFilamentShortagesQueryHandler(
    IPrintJobRepository jobs, IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>> stockQuery)
    : IQueryHandler<GetFilamentShortagesQuery, IReadOnlyList<FilamentShortage>>
{
    public async Task<IReadOnlyList<FilamentShortage>> HandleAsync(GetFilamentShortagesQuery query, CancellationToken cancellationToken)
    {
        var stock = await stockQuery.HandleAsync(new GetFilamentStockQuery(), cancellationToken);
        var queued = new Dictionary<Guid, decimal>();
        foreach (var job in await jobs.ListUnfinishedFiniteAsync(cancellationToken))
        {
            if (job.GetProductionPart() is not { } part) continue;
            var requirements = job.Filaments.Select(line => line.Requirement).ToList();
            foreach (var requirement in requirements)
            {
                if (ProductionJobRequirements.EstimatedGrams(part, requirement, requirements.Count, job, job.FileRevisionHash) is not { } grams) continue;
                var colors = requirement.NarrowFilamentVariantId is { } variantId
                    ? stock.Where(s => s.FilamentVariantId == variantId)
                    : stock.Where(s => requirement.MaterialType is not null && requirement.DetectedColorHex is not null
                        && s.MaterialType == requirement.MaterialType
                        && string.Equals(s.ColorHex, requirement.DetectedColorHex, StringComparison.OrdinalIgnoreCase));
                foreach (var color in colors)
                    queued[color.FilamentVariantId] = queued.GetValueOrDefault(color.FilamentVariantId) + grams;
            }
        }

        return stock.Where(s => queued.GetValueOrDefault(s.FilamentVariantId) > s.Stock.OnHandGrams)
            .Select(s => new FilamentShortage(s.FilamentVariantId, queued[s.FilamentVariantId], s.Stock.OnHandGrams))
            .ToList();
    }
}
