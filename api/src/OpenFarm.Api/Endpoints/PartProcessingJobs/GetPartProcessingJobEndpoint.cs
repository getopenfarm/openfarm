using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Api.Endpoints.PartProcessingJobs;

public sealed class PreviewFilamentRequirementResponse
{
    public int RequirementIndex { get; set; }
    public string? MaterialType { get; set; }
    public string? DetectedColorHex { get; set; }
    public string? DetectedSubBrand { get; set; }
}

public sealed class ThumbnailResponse
{
    public int PlateIndex { get; set; }
    public string ThumbnailBase64 { get; set; } = string.Empty;
}

public sealed class SlicerResponse
{
    public string Engine { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public static SlicerResponse? FromView(SlicerSelectionView? view) =>
        view is null ? null : new SlicerResponse { Engine = view.Engine, Version = view.Version };
}

public sealed class PartProcessingJobPreviewResponse
{
    public decimal? NozzleDiameterMm { get; set; }
    public List<PreviewFilamentRequirementResponse> FilamentRequirements { get; set; } = [];
    public string EstimateStatus { get; set; } = string.Empty;
    public int? EstimatedPrintTimeMinutes { get; set; }
    public decimal? EstimatedFilamentWeightGrams { get; set; }
    public int? PlateCount { get; set; }
    public List<ThumbnailResponse> Thumbnails { get; set; } = [];
}

public sealed class PartProcessingJobResponse
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? PartId { get; set; }
    public SlicerResponse? Slicer { get; set; }
    public PartProcessingJobPreviewResponse? Preview { get; set; }
    public string? ErrorMessage { get; set; }

    public static PartProcessingJobResponse FromDetail(PartProcessingJobDetail detail) => new()
    {
        Id = detail.Id,
        Kind = detail.Kind,
        Status = detail.Status,
        PartId = detail.PartId,
        Slicer = SlicerResponse.FromView(detail.Slicer),
        ErrorMessage = detail.ErrorMessage,
        Preview = detail.Preview is null ? null : new PartProcessingJobPreviewResponse
        {
            NozzleDiameterMm = detail.Preview.NozzleDiameterMm,
            EstimateStatus = detail.Preview.EstimateStatus,
            EstimatedPrintTimeMinutes = detail.Preview.EstimatedPrintTimeMinutes,
            EstimatedFilamentWeightGrams = detail.Preview.EstimatedFilamentWeightGrams,
            PlateCount = detail.Preview.PlateCount,
            FilamentRequirements = detail.Preview.FilamentRequirements.Select(r => new PreviewFilamentRequirementResponse
            {
                RequirementIndex = r.RequirementIndex,
                MaterialType = r.MaterialType,
                DetectedColorHex = r.DetectedColorHex,
                DetectedSubBrand = r.DetectedSubBrand,
            }).ToList(),
            Thumbnails = detail.Preview.Thumbnails.Select(t => new ThumbnailResponse
            {
                PlateIndex = t.PlateIndex,
                ThumbnailBase64 = t.ThumbnailBase64,
            }).ToList(),
        },
    };
}

[Permission("parts:read")]
public sealed class GetPartProcessingJobEndpoint(IQueryHandler<GetPartProcessingJobQuery, PartProcessingJobDetail?> handler)
    : EndpointWithoutRequest<PartProcessingJobResponse>
{
    public override void Configure()
    {
        Get("/api/v1/part-processing-jobs/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetPartProcessingJobQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = PartProcessingJobResponse.FromDetail(detail);
    }
}
