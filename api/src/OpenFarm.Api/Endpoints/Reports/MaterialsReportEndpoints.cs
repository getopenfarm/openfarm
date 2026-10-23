using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Reports;

public sealed class MaterialsReportRequest
{
    /// <summary>Prints that closed, and spools written off, at or after this instant.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>…and before this instant.</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Only this filament's colours; the row for lines with no spool drops out.</summary>
    public Guid? FilamentId { get; set; }

    internal GetMaterialsReportQuery ToQuery() => new(From?.ToUniversalTime(), To?.ToUniversalTime(), FilamentId);
}

public sealed class MaterialsReportRowResponse
{
    /// <summary>Null on the row for lines no spool was deducted from, and on the total.</summary>
    public Guid? FilamentVariantId { get; set; }

    public string? VendorName { get; set; }

    public string? Material { get; set; }

    public string? ColorName { get; set; }

    public string? ColorHex { get; set; }

    /// <summary>Grams completed prints took.</summary>
    public decimal UsedGrams { get; set; }

    /// <summary>Grams failed and cancelled prints took.</summary>
    public decimal WasteGrams { get; set; }

    /// <summary>Waste over all the grams prints took, 0 to 1; null when they took none.</summary>
    public decimal? WasteShare { get; set; }

    /// <summary>What finishing spools wrote off, net of restores.</summary>
    public decimal WrittenOffGrams { get; set; }

    /// <summary>What the print grams cost at the prices recorded when they were deducted. Partial when <see cref="UnpricedGrams"/> is above 0.</summary>
    public decimal Cost { get; set; }

    public decimal UnpricedGrams { get; set; }

    public int PrintCount { get; set; }

    /// <summary>Print lines automatic deduction skipped, and left without grams.</summary>
    public int NotDeductedCount { get; set; }

    public static MaterialsReportRowResponse FromRow(MaterialsReportRow row) => new()
    {
        FilamentVariantId = row.Color?.FilamentVariantId,
        VendorName = row.Color?.VendorName,
        Material = row.Color?.Material,
        ColorName = row.Color?.ColorName,
        ColorHex = row.Color?.ColorHex,
        UsedGrams = row.UsedGrams,
        WasteGrams = row.WasteGrams,
        WasteShare = row.WasteShare,
        WrittenOffGrams = row.WrittenOffGrams,
        Cost = row.Cost,
        UnpricedGrams = row.UnpricedGrams,
        PrintCount = row.PrintCount,
        NotDeductedCount = row.NotDeductedCount,
    };
}

public sealed class MaterialsReportResponse
{
    /// <summary>A row per colour, most grams first, then the row for lines with no spool.</summary>
    public IReadOnlyList<MaterialsReportRowResponse> Rows { get; set; } = [];

    /// <summary>Every row added up; a print that used several colours counts once.</summary>
    public MaterialsReportRowResponse Total { get; set; } = new();
}

/// <summary>GET /api/v1/reports/materials — filament used, wasted and written off per colour in the range.</summary>
[Permission("spools:list")]
public sealed class GetMaterialsReportEndpoint(IQueryHandler<GetMaterialsReportQuery, MaterialsReport> handler)
    : Endpoint<MaterialsReportRequest, MaterialsReportResponse>
{
    public override void Configure()
    {
        Get("/api/v1/reports/materials");
        Policies("Permission");
    }

    public override async Task HandleAsync(MaterialsReportRequest req, CancellationToken ct)
    {
        var report = await handler.HandleAsync(req.ToQuery(), ct);
        Response = new MaterialsReportResponse
        {
            Rows = report.Rows.Select(MaterialsReportRowResponse.FromRow).ToList(),
            Total = MaterialsReportRowResponse.FromRow(report.Total),
        };
    }
}

/// <summary>GET /api/v1/reports/materials.csv — the same report as a CSV file, with a Total row.</summary>
[Permission("spools:list")]
public sealed class ExportMaterialsReportEndpoint(IQueryHandler<GetMaterialsReportQuery, MaterialsReport> handler)
    : Endpoint<MaterialsReportRequest>
{
    public override void Configure()
    {
        Get("/api/v1/reports/materials.csv");
        Policies("Permission");
    }

    public override async Task HandleAsync(MaterialsReportRequest req, CancellationToken ct)
    {
        var report = await handler.HandleAsync(req.ToQuery(), ct);
        var csv = Csv.Write(
            ["Vendor", "Material", "Color", "Used (g)", "Waste (g)", "Waste %", "Written off (g)", "Cost", "Unpriced (g)", "Prints", "Not deducted"],
            report.Rows.Select(row => Fields(row, row.Color?.Material ?? "No spool")).Append(Fields(report.Total, "Total")));
        await Send.BytesAsync(csv, "materials-report.csv", "text/csv", cancellation: ct);
    }

    private static object?[] Fields(MaterialsReportRow row, string material) =>
    [
        row.Color?.VendorName, material, row.Color?.ColorName, Math.Round(row.UsedGrams, 1), Math.Round(row.WasteGrams, 1),
        row.WasteShare is { } share ? Math.Round(share * 100, 1) : null, Math.Round(row.WrittenOffGrams, 1), Math.Round(row.Cost, 2),
        Math.Round(row.UnpricedGrams, 1), row.PrintCount, row.NotDeductedCount,
    ];
}
