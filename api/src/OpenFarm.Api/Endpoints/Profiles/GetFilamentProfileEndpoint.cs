using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class FilamentProfileDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public Guid? BaseProfileId { get; set; }
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public string RawDataJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? Material { get; set; }
    public string? Brand { get; set; }
    public string? ColorHex { get; set; }
    public double? NozzleSizeMm { get; set; }
    public double? NozzleTempC { get; set; }
    public double? BedTempC { get; set; }
    public double? MaxVolumetricSpeedMm3s { get; set; }
    public int? FanPercent { get; set; }
    public List<string> CompatiblePrinterNames { get; set; } = [];

    public static FilamentProfileDetailResponse FromDetail(FilamentProfileDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        SourceFormat = detail.SourceFormat.ToString(),
        BaseProfileId = detail.BaseProfileId,
        BaseProfileName = detail.BaseProfileName,
        HasUnresolvedBaseInChain = detail.HasUnresolvedBaseInChain,
        RawDataJson = detail.RawDataJson,
        CreatedAtUtc = detail.CreatedAtUtc,
        Material = detail.Material,
        Brand = detail.Brand,
        ColorHex = detail.ColorHex,
        NozzleSizeMm = detail.NozzleSizeMm,
        NozzleTempC = detail.NozzleTempC,
        BedTempC = detail.BedTempC,
        MaxVolumetricSpeedMm3s = detail.MaxVolumetricSpeedMm3s,
        FanPercent = detail.FanPercent,
        CompatiblePrinterNames = detail.CompatiblePrinterNames.ToList(),
    };
}

// GET /api/v1/profiles/filament/{id} (FR-011).
[Permission("profiles:get-filament")]
public sealed class GetFilamentProfileEndpoint(IQueryHandler<GetFilamentProfileQuery, FilamentProfileDetail?> handler)
    : EndpointWithoutRequest<FilamentProfileDetailResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/filament/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetFilamentProfileQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = FilamentProfileDetailResponse.FromDetail(detail);
    }
}
