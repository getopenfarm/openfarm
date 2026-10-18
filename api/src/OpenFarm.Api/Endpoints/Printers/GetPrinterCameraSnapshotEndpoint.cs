using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

[Permission("printers:list")]
public sealed class GetPrinterCameraSnapshotEndpoint(IPrinterRepository printers, IPrinterCameraSnapshotStore snapshots)
    : EndpointWithoutRequest<PrinterCameraSnapshot>
{
    public override void Configure()
    {
        Get("/api/v1/printers/{id}/camera-snapshot");
        Policies("Permission");
        Description(b => b.Produces<PrinterCameraSnapshot>(200).Produces(204).Produces(404));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var printer = await printers.GetByIdAsync(Route<Guid>("id"), ct);
        if (printer is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }
        var snapshot = await snapshots.GetAsync(printer.Id, ct);
        if (snapshot is null)
        {
            await HttpContext.Response.SendNoContentAsync(ct);
            return;
        }
        Response = snapshot;
    }
}
