using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class ProcessProfileResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
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
        SourceFormat = summary.SourceFormat.ToString(),
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
    public List<ProcessProfileResponse> Profiles { get; set; } = [];
}

// GET /api/v1/profiles/process.
[Permission("profiles:list-process")]
public sealed class ListProcessProfilesEndpoint(IQueryHandler<GetProcessProfilesQuery, IReadOnlyList<ProcessProfileSummary>> handler)
    : EndpointWithoutRequest<ListProcessProfilesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/process");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var summaries = await handler.HandleAsync(new GetProcessProfilesQuery(), ct);
        Response = new ListProcessProfilesResponse { Profiles = summaries.Select(ProcessProfileResponse.FromSummary).ToList() };
    }
}
