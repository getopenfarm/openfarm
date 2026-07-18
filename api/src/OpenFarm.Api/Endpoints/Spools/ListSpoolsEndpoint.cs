using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class ListSpoolsRequest
{
    public SpoolState? State { get; set; }

    public Guid? FilamentId { get; set; }

    public Guid? FilamentVariantId { get; set; }

    public Guid? VendorId { get; set; }
}

public sealed class ListSpoolsResponse
{
    public IReadOnlyList<SpoolResponse> Spools { get; set; } = [];
}

/// <summary>FR-015 — filters by state, filament, color (variant), and vendor.</summary>
[Permission("spools:list")]
public sealed class ListSpoolsEndpoint(IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> handler)
    : Endpoint<ListSpoolsRequest, ListSpoolsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/spools");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListSpoolsRequest req, CancellationToken ct)
    {
        var spools = await handler.HandleAsync(new GetSpoolsQuery(req.State, req.FilamentId, req.FilamentVariantId, req.VendorId), ct);

        Response = new ListSpoolsResponse
        {
            Spools = spools.Select(SpoolResponse.FromSummary).ToList(),
        };
    }
}
