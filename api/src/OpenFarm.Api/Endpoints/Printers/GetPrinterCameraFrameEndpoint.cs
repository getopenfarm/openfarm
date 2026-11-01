using System.Globalization;
using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

/// <summary>
/// The printer's latest camera frame as a plain JPEG. A visible camera tile polls this about once a second; each call
/// keeps that printer's camera running (<see cref="CameraDemand"/>), and an unchanged frame answers 304 with no body.
/// </summary>
[Permission("printers:list")]
public sealed class GetPrinterCameraFrameEndpoint(IPrinterRepository printers, IPrinterCameraSnapshotStore snapshots, CameraDemand demand)
    : EndpointWithoutRequest
{
    public const string ReceivedAtHeader = "X-Frame-Received-At";

    public override void Configure()
    {
        Get("/api/v1/printers/{id}/camera.jpg");
        Policies("Permission");
        Description(b => b.Produces<byte[]>(200, "image/jpeg").Produces(204).Produces(304).Produces(404));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var response = HttpContext.Response;
        // Browser-private, revalidated every time: the ETag makes an unchanged frame a bodyless 304.
        response.Headers.CacheControl = "private, no-cache";
        var printer = await printers.GetByIdAsync(Route<Guid>("id"), ct);
        if (printer is null)
        {
            await response.SendNotFoundAsync(ct);
            return;
        }
        demand.Touch(printer.Id);
        var snapshot = await snapshots.GetAsync(printer.Id, ct);
        if (snapshot is null)
        {
            await response.SendNoContentAsync(ct);
            return;
        }

        var etag = $"\"{snapshot.ReceivedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}\"";
        response.Headers.ETag = etag;
        response.Headers.LastModified = snapshot.ReceivedAtUtc.ToString("R", CultureInfo.InvariantCulture);
        response.Headers[ReceivedAtHeader] = snapshot.ReceivedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        if (HttpContext.Request.Headers.IfNoneMatch.Contains(etag))
        {
            await response.SendStatusCodeAsync(StatusCodes.Status304NotModified, ct);
            return;
        }
        response.ContentType = "image/jpeg";
        response.ContentLength = snapshot.FrameJpeg.Length;
        await response.Body.WriteAsync(snapshot.FrameJpeg, ct);
    }
}
