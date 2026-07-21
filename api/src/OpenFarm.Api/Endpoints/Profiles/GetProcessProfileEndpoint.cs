using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class ProcessProfileDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public Guid? BaseProfileId { get; set; }
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public string RawDataJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public double? LayerHeightMm { get; set; }
    public int? WallLoops { get; set; }
    public List<string> CompatiblePrinterNames { get; set; } = [];

    public static ProcessProfileDetailResponse FromDetail(ProcessProfileDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        SourceFormat = detail.SourceFormat.ToString(),
        BaseProfileId = detail.BaseProfileId,
        BaseProfileName = detail.BaseProfileName,
        HasUnresolvedBaseInChain = detail.HasUnresolvedBaseInChain,
        RawDataJson = detail.RawDataJson,
        CreatedAtUtc = detail.CreatedAtUtc,
        LayerHeightMm = detail.LayerHeightMm,
        WallLoops = detail.WallLoops,
        CompatiblePrinterNames = detail.CompatiblePrinterNames.ToList(),
    };
}

// GET /api/v1/profiles/process/{id}. 404 on an unknown/cross-tenant id — Finbuckle's global query
// filter makes this indistinguishable from "doesn't exist" (same as every other tenant-scoped
// lookup in this codebase).
[Permission("profiles:get-process")]
public sealed class GetProcessProfileEndpoint(IQueryHandler<GetProcessProfileQuery, ProcessProfileDetail?> handler)
    : EndpointWithoutRequest<ProcessProfileDetailResponse>
{
    public override void Configure()
    {
        Get("/api/v1/profiles/process/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetProcessProfileQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = ProcessProfileDetailResponse.FromDetail(detail);
    }
}
