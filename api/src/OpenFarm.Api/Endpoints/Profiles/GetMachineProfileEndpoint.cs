using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class MachineProfileDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public Guid? BaseProfileId { get; set; }
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public string RawDataJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public double? NozzleSizeMm { get; set; }
    public double? BedSizeXMm { get; set; }
    public double? BedSizeYMm { get; set; }
    public double? MaxHeightMm { get; set; }
    public bool? HasEnclosure { get; set; }
    public List<string> SupportedMaterials { get; set; } = [];

    public static MachineProfileDetailResponse FromDetail(MachineProfileDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        SourceFormat = detail.SourceFormat.ToString(),
        BaseProfileId = detail.BaseProfileId,
        BaseProfileName = detail.BaseProfileName,
        HasUnresolvedBaseInChain = detail.HasUnresolvedBaseInChain,
        RawDataJson = detail.RawDataJson,
        CreatedAtUtc = detail.CreatedAtUtc,
        NozzleSizeMm = detail.NozzleSizeMm,
        BedSizeXMm = detail.BedSizeXMm,
        BedSizeYMm = detail.BedSizeYMm,
        MaxHeightMm = detail.MaxHeightMm,
        HasEnclosure = detail.HasEnclosure,
        SupportedMaterials = detail.SupportedMaterials.ToList(),
    };
}

// GET /api/v1/profiles/machine/{id} (FR-011). 404 on an unknown/cross-tenant id — Finbuckle's
// global query filter makes this indistinguishable from "doesn't exist" (same as every other
// tenant-scoped lookup in this codebase).
[Permission("profiles:get-machine")]
public sealed class GetMachineProfileEndpoint(IQueryHandler<GetMachineProfileQuery, MachineProfileDetail?> handler)
    : EndpointWithoutRequest<MachineProfileDetailResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/machine/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetMachineProfileQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = MachineProfileDetailResponse.FromDetail(detail);
    }
}
