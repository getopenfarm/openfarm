using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Filament by colour over [<see cref="From"/>, <see cref="To"/>): prints that closed in it, spools written off in it. Null is open-ended.
/// With <see cref="FilamentId"/>, only that filament's colours, so lines no spool was deducted from drop out.
/// </summary>
public sealed record GetMaterialsReportQuery(DateTimeOffset? From, DateTimeOffset? To, Guid? FilamentId = null);

/// <summary>
/// One print line deduction ran on: grams recorded, or <see cref="Skipped"/> with none. <see cref="FilamentVariantId"/> is the
/// colour of the spool it was deducted from; null when it had no spool.
/// </summary>
public sealed record MaterialsReportLine(
    Guid PrintId, PrintJobStatus Status, Guid? FilamentVariantId, decimal? Grams, decimal? PricePerKg, bool Skipped);

public sealed record MaterialsReportColor(Guid FilamentVariantId, string VendorName, string Material, string ColorName, string ColorHex);

/// <summary>
/// One colour's figures, or (<see cref="Color"/> null) the lines no spool was deducted from. Used is completed prints, waste is
/// failed and cancelled ones, written off is finished spools net of restores. <see cref="Cost"/> counts only grams with a recorded
/// price; <see cref="UnpricedGrams"/> are the rest, and make it partial.
/// </summary>
public sealed record MaterialsReportRow(
    MaterialsReportColor? Color,
    decimal UsedGrams,
    decimal WasteGrams,
    decimal WrittenOffGrams,
    decimal Cost,
    decimal UnpricedGrams,
    int PrintCount,
    int NotDeductedCount)
{
    /// <summary>Waste over all the grams prints took, 0 to 1; null when they took none.</summary>
    public decimal? WasteShare => UsedGrams + WasteGrams is > 0 and var printed ? WasteGrams / printed : null;

    public static MaterialsReportRow Of(MaterialsReportColor? color, IReadOnlyCollection<MaterialsReportLine> lines, decimal writtenOffGrams)
    {
        decimal Grams(Func<MaterialsReportLine, bool> include) => lines.Where(include).Sum(l => l.Grams ?? 0);

        return new(
            color,
            Grams(l => l.Status == PrintJobStatus.Completed),
            Grams(l => l.Status is PrintJobStatus.Failed or PrintJobStatus.Cancelled),
            writtenOffGrams,
            lines.Sum(l => l.Grams is { } grams && l.PricePerKg is { } price ? grams * price / 1000m : 0),
            Grams(l => l.PricePerKg is null),
            lines.Select(l => l.PrintId).Distinct().Count(),
            lines.Count(l => l.Skipped));
    }
}

/// <summary>A row per colour, most grams first, then the no-spool row; <see cref="Total"/> counts a print once however many colours it used.</summary>
public sealed record MaterialsReport(IReadOnlyList<MaterialsReportRow> Rows, MaterialsReportRow Total)
{
    public static MaterialsReport Of(
        IReadOnlyCollection<MaterialsReportLine> lines,
        IReadOnlyDictionary<Guid, decimal> writtenOffGrams,
        IReadOnlyDictionary<Guid, MaterialsReportColor> colors)
    {
        var rows = lines.Select(l => l.FilamentVariantId).Concat(writtenOffGrams.Keys.Select(id => (Guid?)id)).Distinct()
            .Select(id => MaterialsReportRow.Of(
                id is { } variantId ? colors[variantId] : null,
                lines.Where(l => l.FilamentVariantId == id).ToList(),
                id is { } written ? writtenOffGrams.GetValueOrDefault(written) : 0))
            .OrderBy(r => r.Color is null)
            .ThenByDescending(r => r.UsedGrams + r.WasteGrams + r.WrittenOffGrams)
            .ToList();
        return new(rows, MaterialsReportRow.Of(null, lines, writtenOffGrams.Values.Sum()));
    }
}

public sealed class GetMaterialsReportQueryHandler(ISpoolReadModel spoolReadModel) : IQueryHandler<GetMaterialsReportQuery, MaterialsReport>
{
    public Task<MaterialsReport> HandleAsync(GetMaterialsReportQuery query, CancellationToken cancellationToken) =>
        spoolReadModel.GetMaterialsReportAsync(query, cancellationToken);
}
