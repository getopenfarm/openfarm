using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class FilamentProfileResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
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
    public List<FilamentProfileResponse> Profiles { get; set; } = [];
}

// GET /api/v1/profiles/filament (FR-010, US3).
[Permission("profiles:list-filament")]
public sealed class ListFilamentProfilesEndpoint(IQueryHandler<GetFilamentProfilesQuery, IReadOnlyList<FilamentProfileSummary>> handler)
    : EndpointWithoutRequest<ListFilamentProfilesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/filament");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var summaries = await handler.HandleAsync(new GetFilamentProfilesQuery(), ct);
        Response = new ListFilamentProfilesResponse { Profiles = summaries.Select(FilamentProfileResponse.FromSummary).ToList() };
    }
}
