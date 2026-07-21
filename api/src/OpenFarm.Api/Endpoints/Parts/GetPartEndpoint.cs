using FastEndpoints;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class PartFilamentRequirementResponse
{
    public Guid Id { get; set; }
    public string? MaterialType { get; set; }
    public string? DetectedColorHex { get; set; }
    public string? DetectedSubBrand { get; set; }
    public Guid? NarrowFilamentVariantId { get; set; }
    public Guid? NarrowFilamentProfileId { get; set; }
    public bool NarrowFilamentVariantUnresolved { get; set; }
    public bool NarrowFilamentProfileUnresolved { get; set; }
}

public sealed class PartResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileFormat { get; set; } = string.Empty;
    public bool RequiresSlicing { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public decimal NozzleDiameterMm { get; set; }
    public string EstimateStatus { get; set; } = string.Empty;
    public int? EstimatedPrintTimeMinutes { get; set; }
    public decimal? EstimatedFilamentWeightGrams { get; set; }
    public List<PartFilamentRequirementResponse> FilamentRequirements { get; set; } = [];
    public int PlateCount { get; set; }
    public List<ThumbnailResponse> Thumbnails { get; set; } = [];
    public List<string> Tags { get; set; } = [];

    public static PartResponse FromDetail(PartDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        FileFormat = detail.FileFormat,
        RequiresSlicing = detail.RequiresSlicing,
        OriginalFileName = detail.OriginalFileName,
        FileSizeBytes = detail.FileSizeBytes,
        NozzleDiameterMm = detail.NozzleDiameterMm,
        EstimateStatus = detail.EstimateStatus,
        EstimatedPrintTimeMinutes = detail.EstimatedPrintTimeMinutes,
        EstimatedFilamentWeightGrams = detail.EstimatedFilamentWeightGrams,
        PlateCount = detail.PlateCount,
        FilamentRequirements = detail.FilamentRequirements.Select(r => new PartFilamentRequirementResponse
        {
            Id = r.Id,
            MaterialType = r.MaterialType,
            DetectedColorHex = r.DetectedColorHex,
            DetectedSubBrand = r.DetectedSubBrand,
            NarrowFilamentVariantId = r.NarrowFilamentVariantId,
            NarrowFilamentProfileId = r.NarrowFilamentProfileId,
            NarrowFilamentVariantUnresolved = r.NarrowFilamentVariantUnresolved,
            NarrowFilamentProfileUnresolved = r.NarrowFilamentProfileUnresolved,
        }).ToList(),
        Thumbnails = detail.Thumbnails.Select(t => new ThumbnailResponse { PlateIndex = t.PlateIndex, ThumbnailBase64 = t.ThumbnailBase64 }).ToList(),
        Tags = detail.Tags.ToList(),
    };

    /// <summary>
    /// Used by Confirm/Replace, which return the domain entity directly rather than round-tripping
    /// through <c>GetPartQuery</c>. Doesn't compute the narrow-override unresolved flags (defaults
    /// false) — acceptable since a just-confirmed override was, by definition, valid a moment ago.
    /// </summary>
    public static PartResponse FromDomain(Part part) => new()
    {
        Id = part.Id,
        Name = part.Name,
        FileFormat = part.FileFormat.ToString(),
        RequiresSlicing = part.RequiresSlicing,
        OriginalFileName = part.OriginalFileName,
        FileSizeBytes = part.FileSizeBytes,
        NozzleDiameterMm = part.NozzleDiameterMm,
        EstimateStatus = part.EstimateStatus.ToString(),
        EstimatedPrintTimeMinutes = part.EstimatedPrintTimeMinutes,
        EstimatedFilamentWeightGrams = part.EstimatedFilamentWeightGrams,
        PlateCount = part.PlateCount,
        FilamentRequirements = part.FilamentRequirements.Select(r => new PartFilamentRequirementResponse
        {
            Id = r.Id,
            MaterialType = r.MaterialType,
            DetectedColorHex = r.DetectedColorHex,
            DetectedSubBrand = r.DetectedSubBrand,
            NarrowFilamentVariantId = r.NarrowFilamentVariantId,
            NarrowFilamentProfileId = r.NarrowFilamentProfileId,
        }).ToList(),
        Thumbnails = part.Thumbnails.OrderBy(t => t.PlateIndex).Select(t => new ThumbnailResponse { PlateIndex = t.PlateIndex, ThumbnailBase64 = Convert.ToBase64String(t.Bytes) }).ToList(),
        Tags = part.Tags.Select(t => t.Name).ToList(),
    };
}

// GET /api/v1/parts/{id} (contracts/part-library-api.md).
[Permission("parts:read")]
public sealed class GetPartEndpoint(IQueryHandler<GetPartQuery, PartDetail?> handler) : EndpointWithoutRequest<PartResponse>
{
    public override void Configure()
    {
        Get("/api/v1/parts/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetPartQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = PartResponse.FromDetail(detail);
    }
}
