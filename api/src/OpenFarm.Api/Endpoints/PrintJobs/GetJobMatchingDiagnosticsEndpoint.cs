using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.PrintJobs;

[Permission("print-jobs:view")]
public sealed class GetJobMatchingDiagnosticsEndpoint(IPrintJobRepository jobs, IPrintJobEligibilityQuery eligibility)
    : EndpointWithoutRequest<JobMatchingDiagnostics>
{
    public override void Configure()
    {
        Get("/api/v1/print-jobs/{id}/matching-diagnostics");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var job = await jobs.GetByIdAsync(Route<Guid>("id"), ct);
        if (job is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }
        Response = await eligibility.GetDiagnosticsAsync(job, ct);
    }
}
