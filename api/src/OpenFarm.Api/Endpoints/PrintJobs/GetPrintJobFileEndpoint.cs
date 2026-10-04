using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.PrintJobs;

[Permission("print-jobs:view")]
public sealed class GetPrintJobFileEndpoint(IQueryHandler<GetPrintJobFileQuery, JobFileContent> handler) : EndpointWithoutRequest
{
    public override void Configure() { Get("/api/v1/print-jobs/{id}/file"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var file = await handler.HandleAsync(new GetPrintJobFileQuery(Route<Guid>("id")), ct);
        await Send.BytesAsync(file.Bytes, file.FileName, "application/octet-stream", cancellation: ct);
    }
}
