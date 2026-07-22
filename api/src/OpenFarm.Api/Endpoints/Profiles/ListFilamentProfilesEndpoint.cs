using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class ListFilamentProfilesRequest
{
    public string? Origin { get; set; }
    public string? Q { get; set; }
    public string? CompatibleWithMachineName { get; set; }
    public string? MaterialType { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public sealed class FilamentProfileResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public string? MaterialType { get; set; }
    public string SourceFormat { get; set; } = string.Empty;
    public Guid? BaseProfileId { get; set; }
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public string? Material { get; set; }
    public string? Brand { get; set; }
    public string? ColorHex { get; set; }
    public double? NozzleSizeMm { get; set; }
    public double? NozzleTempC { get; set; }
    public double? BedTempC { get; set; }
    public double? MaxVolumetricSpeedMm3s { get; set; }
    public int? FanPercent { get; set; }
    public List<string> CompatiblePrinterNames { get; set; } = [];

    public static FilamentProfileResponse FromSummary(FilamentProfileSummary summary) => new()
    {
        Id = summary.Id,
        Name = summary.Name,
        Origin = summary.Origin.ToString(),
        VendorName = summary.VendorName,
        MaterialType = summary.MaterialType,
        SourceFormat = summary.SourceFormat.ToString(),
        BaseProfileId = summary.BaseProfileId,
        BaseProfileName = summary.BaseProfileName,
        HasUnresolvedBaseInChain = summary.HasUnresolvedBaseInChain,
        Material = summary.Material,
        Brand = summary.Brand,
        ColorHex = summary.ColorHex,
        NozzleSizeMm = summary.NozzleSizeMm,
        NozzleTempC = summary.NozzleTempC,
        BedTempC = summary.BedTempC,
        MaxVolumetricSpeedMm3s = summary.MaxVolumetricSpeedMm3s,
        FanPercent = summary.FanPercent,
        CompatiblePrinterNames = summary.CompatiblePrinterNames.ToList(),
    };
}

public sealed class ListFilamentProfilesResponse
{
    public List<FilamentProfileResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

// GET /api/v1/profiles/filament (FR-010, US3; extended for US2/US4 picker use, contracts/profiles-api.md).
[Permission("profiles:list-filament")]
public sealed class ListFilamentProfilesEndpoint(IQueryHandler<GetFilamentProfilesQuery, ProfilePage<FilamentProfileSummary>> handler)
    : Endpoint<ListFilamentProfilesRequest, ListFilamentProfilesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/filament");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListFilamentProfilesRequest req, CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new GetFilamentProfilesQuery(req.Origin, req.Q, req.CompatibleWithMachineName, req.MaterialType, req.Page, req.PageSize), ct);

        Response = new ListFilamentProfilesResponse
        {
            Items = result.Items.Select(FilamentProfileResponse.FromSummary).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
        };
    }
}
