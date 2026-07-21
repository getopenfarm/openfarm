using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class ListPartsRequest
{
    public List<string>? Tag { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed class PartSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileFormat { get; set; } = string.Empty;
    public bool RequiresSlicing { get; set; }
    public decimal NozzleDiameterMm { get; set; }
    public string EstimateStatus { get; set; } = string.Empty;
    public int? EstimatedPrintTimeMinutes { get; set; }
    public decimal? EstimatedFilamentWeightGrams { get; set; }
    public string? ThumbnailBase64 { get; set; }
    public List<string> MaterialTypes { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}

public sealed class ListPartsResponse
{
    public List<PartSummaryResponse> Parts { get; set; } = [];
    public int TotalCount { get; set; }
}

// GET /api/v1/parts (contracts/part-library-api.md).
[Permission("parts:list")]
public sealed class ListPartsEndpoint(IQueryHandler<GetPartsQuery, PartsPage> handler) : Endpoint<ListPartsRequest, ListPartsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/parts");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListPartsRequest req, CancellationToken ct)
    {
        var page = Math.Max(req.Page, 1);
        var pageSize = req.PageSize is < 1 or > 200 ? 25 : req.PageSize;

        var result = await handler.HandleAsync(new GetPartsQuery(req.Tag ?? [], page, pageSize), ct);

        Response = new ListPartsResponse
        {
            TotalCount = result.TotalCount,
            Parts = result.Parts.Select(p => new PartSummaryResponse
            {
                Id = p.Id,
                Name = p.Name,
                FileFormat = p.FileFormat,
                RequiresSlicing = p.RequiresSlicing,
                NozzleDiameterMm = p.NozzleDiameterMm,
                EstimateStatus = p.EstimateStatus,
                EstimatedPrintTimeMinutes = p.EstimatedPrintTimeMinutes,
                EstimatedFilamentWeightGrams = p.EstimatedFilamentWeightGrams,
                ThumbnailBase64 = p.ThumbnailBase64,
                MaterialTypes = p.MaterialTypes.ToList(),
                Tags = p.Tags.ToList(),
            }).ToList(),
        };
    }
}
