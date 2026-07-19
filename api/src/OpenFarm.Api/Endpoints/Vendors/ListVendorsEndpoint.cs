using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Vendors;

public sealed class ListVendorsResponse
{
    public IReadOnlyList<VendorResponse> Vendors { get; set; } = [];
}

[Permission("vendors:list")]
public sealed class ListVendorsEndpoint(IQueryHandler<GetVendorsQuery, IReadOnlyList<VendorSummary>> handler)
    : EndpointWithoutRequest<ListVendorsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/vendors");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var vendors = await handler.HandleAsync(new GetVendorsQuery(), ct);

        Response = new ListVendorsResponse
        {
            Vendors = vendors.Select(VendorResponse.FromSummary).ToList(),
        };
    }
}
