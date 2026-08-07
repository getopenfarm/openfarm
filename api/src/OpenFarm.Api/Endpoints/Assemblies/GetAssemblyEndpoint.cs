using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.Assemblies;

/// <summary>GET /api/v1/assemblies/{id} — one level of structure (research.md R7); client expands a sub-assembly line by calling this again with its targetId.</summary>
[Permission("assemblies:view")]
public sealed class GetAssemblyEndpoint(IQueryHandler<GetAssemblyQuery, AssemblyDetail?> handler) : EndpointWithoutRequest<AssemblyResponse>
{
    public override void Configure()
    {
        Get("/api/v1/assemblies/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetAssemblyQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = AssemblyResponse.FromDetail(detail);
    }
}
