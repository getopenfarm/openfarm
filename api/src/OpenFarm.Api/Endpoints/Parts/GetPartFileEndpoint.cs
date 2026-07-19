using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Api.Endpoints.Parts;

// GET /api/v1/parts/{id}/file — raw file bytes as originally uploaded (contracts/part-library-api.md).
[Permission("parts:read")]
public sealed class GetPartFileEndpoint(IQueryHandler<GetPartFileQuery, PartFileDownload?> handler) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/v1/parts/{id}/file");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var download = await handler.HandleAsync(new GetPartFileQuery(id), ct);
        if (download is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        await using var content = download.Content;
        HttpContext.Response.ContentType = "application/octet-stream";
        HttpContext.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{download.OriginalFileName}\"";
        await content.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
