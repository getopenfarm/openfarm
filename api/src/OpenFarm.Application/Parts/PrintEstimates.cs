using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>Known subtotal plus the number of source contributions that are unknown. Zero is a known value.</summary>
public sealed record EstimateMetric(decimal? Value, int Missing);
/// <param name="FilamentId">Any colour of this catalog filament, when that is the line's choice instead of <paramref name="VariantId"/>.</param>
/// <param name="AnyColor">Any colour of the material, any brand: the file's colour is not the line's.</param>
public sealed record EstimateColor(string? MaterialType, string? ColorHex, Guid? VariantId, decimal? Grams, int Missing = 0, Guid? FilamentId = null,
    bool AnyColor = false);
public sealed record PrintEstimate(EstimateMetric Time, EstimateMetric Filament, bool Approximate, IReadOnlyList<EstimateColor> Colors);

/// <summary>Canonical planning estimates; proportional partial plates are approximate, never actual usage.</summary>
public static class PrintEstimates
{
    public static PrintEstimate ForPart(Part? part, decimal units) => Calculate(units, part?.UnitsPerPrint ?? 1,
        part?.EstimatedPrintTimeMinutes, part?.EstimatedFilamentWeightGrams,
        part?.FilamentRequirements.Select(r => new EstimateColor(r.MaterialType, r.DetectedColorHex, r.NarrowFilamentVariantId, r.EstimatedGrams, FilamentId: r.NarrowFilamentId,
            AnyColor: r.AnyColor)).ToArray() ?? []);

    public static PrintEstimate Calculate(decimal units, int unitsPerPlate, decimal? minutesPerPlate, decimal? fallbackGrams, IReadOnlyList<EstimateColor> colors)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        ArgumentOutOfRangeException.ThrowIfLessThan(unitsPerPlate, 1);
        if (units == 0) return Sum([]);
        var scale = units / unitsPerPlate;
        var time = new EstimateMetric(minutesPerPlate * scale, minutesPerPlate is null ? 1 : 0);
        IReadOnlyList<EstimateColor> weights;
        if (colors.Count > 0 && colors.All(c => c.Grams is not null)) weights = colors;
        else if (fallbackGrams is not null)
        {
            // A scalar cannot be divided between unknown colors or assigned an arbitrary variant.
            var sole = colors.Count == 1 ? colors[0] : new EstimateColor(null, null, null, null);
            weights = [sole with { Grams = fallbackGrams }];
        }
        else weights = colors.Count > 0 ? colors : [new(null, null, null, null)];
        var scaled = weights.Select(c => c with { Grams = c.Grams * scale, Missing = c.Grams is null ? 1 : 0 }).ToArray();
        return new(time, Metric(scaled.Select(c => c.Grams)), units % unitsPerPlate != 0, scaled);
    }

    public static PrintEstimate Sum(IEnumerable<PrintEstimate> estimates)
    {
        var rows = estimates.ToArray();
        var colors = rows.SelectMany(e => e.Colors)
            // A colour-free choice (any colour of a filament, or any colour) has no colour of its own: one row, whatever the files' colours.
            .Select(c => c.VariantId is null && (c.FilamentId is not null || c.AnyColor) ? c with { ColorHex = null } : c)
            .GroupBy(c => c.VariantId is { } id ? $"variant:{id}" : c.FilamentId is { } filament ? $"filament:{filament}"
                : $"{c.MaterialType}|{c.ColorHex?.ToUpperInvariant()}|{c.AnyColor}")
            .Select(group => group.First() with { Grams = Metric(group.Select(c => c.Grams)).Value, Missing = group.Sum(c => c.Missing) }).ToArray();
        return new(SumMetric(rows.Select(e => e.Time)), SumMetric(rows.Select(e => e.Filament)), rows.Any(e => e.Approximate), colors);
    }

    private static EstimateMetric Metric(IEnumerable<decimal?> values)
    {
        var rows = values.ToArray();
        return new(rows.Length == 0 ? 0 : rows.Any(v => v is not null) ? rows.Sum(v => v ?? 0) : null, rows.Count(v => v is null));
    }

    private static EstimateMetric SumMetric(IEnumerable<EstimateMetric> values)
    {
        var rows = values.ToArray();
        return new(Metric(rows.Select(v => v.Value)).Value, rows.Sum(v => v.Missing));
    }
}
