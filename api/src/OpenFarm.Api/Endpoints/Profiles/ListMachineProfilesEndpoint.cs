using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class MachineProfileResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
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
    public List<MachineProfileResponse> Profiles { get; set; } = [];
}

// GET /api/v1/profiles/machine (FR-010, US3).
[Permission("profiles:list-machine")]
public sealed class ListMachineProfilesEndpoint(IQueryHandler<GetMachineProfilesQuery, IReadOnlyList<MachineProfileSummary>> handler)
    : EndpointWithoutRequest<ListMachineProfilesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/machine");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var summaries = await handler.HandleAsync(new GetMachineProfilesQuery(), ct);
        Response = new ListMachineProfilesResponse { Profiles = summaries.Select(MachineProfileResponse.FromSummary).ToList() };
    }
}
