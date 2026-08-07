using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.PrintJobs;

/// <summary>GET /api/v1/print-jobs/{id} — full job detail including FilamentConsumption, FileRevisionHash/Number, timestamps.</summary>
[Permission("print-jobs:view")]
public sealed class GetPrintJobEndpoint(IQueryHandler<GetPrintJobQuery, PrintJobDetail?> handler) : EndpointWithoutRequest<PrintJobResponse>
{
    public override void Configure()
    {
        Get("/api/v1/print-jobs/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetPrintJobQuery(id), ct);
        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = PrintJobResponse.FromDetail(detail);
    }
}
