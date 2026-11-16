using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.ActivityLog;

public sealed class GetPrinterActivityRequest
{
    public Guid Id { get; set; }
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed class PrinterActivityEntryResponse
{
    public ActivityLogDetails? Details { get; set; }

    public Guid Id { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public Guid? ActorId { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public string? ActorDisplayName { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class PrinterActivityPageResponse
{
    public List<PrinterActivityEntryResponse> Items { get; set; } = [];

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }
}

[Permission("printers:get-detail")]
public sealed class GetPrinterActivityEndpoint(IQueryHandler<GetActivityLogQuery, ActivityLogPageResult> handler)
    : Endpoint<GetPrinterActivityRequest, PrinterActivityPageResponse>
{
    public override void Configure()
    {
        Get("/api/v1/printers/{id}/activity");
        Policies("Permission");
    }

    public override async Task HandleAsync(GetPrinterActivityRequest req, CancellationToken ct)
    {
        var page = req.Page < 1 ? 1 : req.Page;
        var pageSize = req.PageSize is < 1 or > 100 ? 25 : req.PageSize;

        var result = await handler.HandleAsync(new GetActivityLogQuery(page, pageSize, "Printer", req.Id.ToString()), ct);

        Response = new PrinterActivityPageResponse
        {
            Items = result.Items
                .Select(e => new PrinterActivityEntryResponse
                {
                    Details = e.Details,
                    Id = e.Id,
                    EntityType = e.EntityType,
                    EntityId = e.EntityId,
                    EventType = e.EventType,
                    ActorId = e.ActorId,
                    ActorType = e.ActorType.ToString(),
                    ActorDisplayName = e.ActorDisplayName,
                    TimestampUtc = e.TimestampUtc,
                })
                .ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
        };
    }
}
