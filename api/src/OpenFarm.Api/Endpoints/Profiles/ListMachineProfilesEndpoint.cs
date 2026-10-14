using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class ListMachineProfilesRequest
{
    public Guid? PrinterId { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
    public string? Origin { get; set; }
    public string? Q { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public sealed class MachineProfileResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public string SourceFormat { get; set; } = string.Empty;
    public Guid? BaseProfileId { get; set; }
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public double? NozzleSizeMm { get; set; }
    public double? BedSizeXMm { get; set; }
    public double? BedSizeYMm { get; set; }
    public double? MaxHeightMm { get; set; }
    public bool? HasEnclosure { get; set; }
    public List<string> SupportedMaterials { get; set; } = [];

    public static MachineProfileResponse FromSummary(MachineProfileSummary summary) => new()
    {
        Id = summary.Id,
        Name = summary.Name,
        Origin = summary.Origin.ToString(),
        VendorName = summary.VendorName,
        SourceFormat = summary.SourceFormat.ToString(),
        BaseProfileId = summary.BaseProfileId,
        BaseProfileName = summary.BaseProfileName,
        HasUnresolvedBaseInChain = summary.HasUnresolvedBaseInChain,
        NozzleSizeMm = summary.NozzleSizeMm,
        BedSizeXMm = summary.BedSizeXMm,
        BedSizeYMm = summary.BedSizeYMm,
        MaxHeightMm = summary.MaxHeightMm,
        HasEnclosure = summary.HasEnclosure,
        SupportedMaterials = summary.SupportedMaterials.ToList(),
    };
}

public sealed class ListMachineProfilesResponse
{
    public List<MachineProfileResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

// GET /api/v1/profiles/machine (FR-010, US3; extended for US2 picker use, contracts/profiles-api.md).
[Permission("profiles:list-machine")]
public sealed class ListMachineProfilesEndpoint(IQueryHandler<GetMachineProfilesQuery, ProfilePage<MachineProfileSummary>> handler)
    : Endpoint<ListMachineProfilesRequest, ListMachineProfilesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/machine");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListMachineProfilesRequest req, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetMachineProfilesQuery(req.Origin, req.Q, req.Page, req.PageSize, req.PrinterId, ListSort.Parse(req.SortBy, req.SortDirection, "name")), ct);

        Response = new ListMachineProfilesResponse
        {
            Items = result.Items.Select(MachineProfileResponse.FromSummary).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
        };
    }
}
