using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class ListPartsRequest
{
    public List<string>? Tag { get; set; }

    public string? Folder { get; set; }

    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed class PartColorGramsResponse
{
    public Guid? NarrowFilamentVariantId { get; set; }

    public string? MaterialType { get; set; }
    public string? ColorHex { get; set; }
    public decimal? Grams { get; set; }
}

public sealed class PartSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileFormat { get; set; } = string.Empty;
    public bool RequiresSlicing { get; set; }
    public decimal? NozzleDiameterMm { get; set; }
    public string PrintRequirementsStatus { get; set; } = "Complete";
    public List<string> ProcessingWarnings { get; set; } = [];
    public string? EstimateUnavailableReason { get; set; }
    public OpenFarm.Domain.Parts.EstimationDetails? Estimation { get; set; }
    public string? EstimationPrinterBrand { get; set; }
    public string? EstimationPrinterModelCode { get; set; }
    public string EstimateStatus { get; set; } = string.Empty;
    public int? EstimatedPrintTimeMinutes { get; set; }
    public decimal? EstimatedFilamentWeightGrams { get; set; }
    public int UnitsPerPrint { get; set; } = 1;
    public string? ThumbnailBase64 { get; set; }
    public List<string> MaterialTypes { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    /// <summary>Per-requirement grams (per plate set, purge included) for by-colour totals.</summary>
    public List<PartColorGramsResponse> FilamentByColor { get; set; } = [];

    public static PartSummaryResponse FromSummary(PartSummary p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        FileFormat = p.FileFormat,
        RequiresSlicing = p.RequiresSlicing,
        NozzleDiameterMm = p.NozzleDiameterMm,
        PrintRequirementsStatus = p.PrintRequirementsStatus,
        ProcessingWarnings = p.ProcessingWarnings?.ToList() ?? [],
        EstimateUnavailableReason = p.EstimateUnavailableReason,
        Estimation = p.Estimation,
        EstimationPrinterBrand = OpenFarm.Domain.Printers.PrinterModelCatalog.TryResolveSlicedModel(p.Estimation?.MachineModel)?.Brand.ToString(),
        EstimationPrinterModelCode = OpenFarm.Domain.Printers.PrinterModelCatalog.TryResolveSlicedModel(p.Estimation?.MachineModel)?.ModelCode,
        EstimateStatus = p.EstimateStatus,
        EstimatedPrintTimeMinutes = p.EstimatedPrintTimeMinutes,
        EstimatedFilamentWeightGrams = p.EstimatedFilamentWeightGrams,
        UnitsPerPrint = p.UnitsPerPrint,
        ThumbnailBase64 = p.ThumbnailBase64,
        MaterialTypes = p.MaterialTypes.ToList(),
        Tags = p.Tags.ToList(),
        FilamentByColor = (p.FilamentByColor ?? []).Select(c => new PartColorGramsResponse { MaterialType = c.MaterialType, ColorHex = c.ColorHex, Grams = c.Grams, NarrowFilamentVariantId = c.NarrowFilamentVariantId }).ToList(),
    };
}

public sealed class ListPartsResponse
{
    public List<PartSummaryResponse> Parts { get; set; } = [];
    public int TotalCount { get; set; }
}

// GET /api/v1/parts (contracts/part-library-api.md).
[Permission("parts:list")]
public sealed class ListPartsEndpoint(IQueryHandler<GetPartsQuery, PartsPage> handler, OpenFarm.Application.Library.ILibraryFolderService folders) : Endpoint<ListPartsRequest, ListPartsResponse>
{
    private static Guid? LibraryLocation(string folder) => OpenFarm.Domain.Library.LibraryFolder.ParseLocation(folder);

    public override void Configure()
    {
        Get("/api/v1/parts");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListPartsRequest req, CancellationToken ct)
    {
        if (req.Folder is not null)
            await folders.ValidateDestinationAsync("parts", LibraryLocation(req.Folder), ct);
        var page = Math.Max(req.Page, 1);
        var pageSize = req.PageSize is < 1 or > 200 ? 25 : req.PageSize;

        var result = await handler.HandleAsync(new GetPartsQuery(req.Tag ?? [], page, pageSize, ListSort.Parse(req.SortBy, req.SortDirection, "name"), req.Folder), ct);

        Response = new ListPartsResponse
        {
            TotalCount = result.TotalCount,
            Parts = result.Parts.Select(PartSummaryResponse.FromSummary).ToList(),
        };
    }
}
