using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class ListProcessProfilesRequest
{
    public string? Origin { get; set; }
    public string? Q { get; set; }
    public string? CompatibleWithMachineName { get; set; }
    public Guid? PrinterId { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public sealed class ProcessProfileResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public string SourceFormat { get; set; } = string.Empty;
    public string? SourceVersion { get; set; }
    public Guid? BaseProfileId { get; set; }
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public double? LayerHeightMm { get; set; }
    public int? WallLoops { get; set; }
    public List<string> CompatiblePrinterNames { get; set; } = [];

    public static ProcessProfileResponse FromSummary(ProcessProfileSummary summary) => new()
    {
        Id = summary.Id,
        Name = summary.Name,
        Origin = summary.Origin.ToString(),
        VendorName = summary.VendorName,
        SourceFormat = summary.SourceFormat.ToString(),
        SourceVersion = summary.SourceVersion,
        BaseProfileId = summary.BaseProfileId,
        BaseProfileName = summary.BaseProfileName,
        HasUnresolvedBaseInChain = summary.HasUnresolvedBaseInChain,
        LayerHeightMm = summary.LayerHeightMm,
        WallLoops = summary.WallLoops,
        CompatiblePrinterNames = summary.CompatiblePrinterNames.ToList(),
    };
}

public sealed class ListProcessProfilesResponse
{
    public List<ProcessProfileResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }

    // Only meaningful on a `printerId`-driven call (contracts/profiles-api.md) — null otherwise.
    public bool? Available { get; set; }
}

// GET /api/v1/profiles/process (extended for US2 picker use, contracts/profiles-api.md).
[Permission("profiles:list-process")]
public sealed class ListProcessProfilesEndpoint(IQueryHandler<GetProcessProfilesQuery, ProfilePage<ProcessProfileSummary>> handler)
    : Endpoint<ListProcessProfilesRequest, ListProcessProfilesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/process");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListProcessProfilesRequest req, CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new GetProcessProfilesQuery(req.Origin, req.Q, req.CompatibleWithMachineName, req.PrinterId, req.Page, req.PageSize), ct);

        Response = new ListProcessProfilesResponse
        {
            Items = result.Items.Select(ProcessProfileResponse.FromSummary).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            Available = result.Available,
        };
    }
}
