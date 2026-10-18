using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.Notifications;

public sealed class NotificationLineResponse
{
    public Guid LineId { get; set; }

    public int ToolIndex { get; set; }

    public string? MaterialType { get; set; }

    public string? ColorHex { get; set; }

    public decimal? EstimatedGrams { get; set; }

    public Guid? SpoolId { get; set; }

    /// <summary>NoSpool, SpoolNotLoaded, NoEstimate or NoProgress; only the first two can be fixed by assigning a spool.</summary>
    public string Reason { get; set; } = string.Empty;
}

public sealed class NotificationItemResponse
{
    /// <summary>"FilamentNotDeducted" today.</summary>
    public string Type { get; set; } = string.Empty;

    public Guid PrintId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>"Dispatched" or "External".</summary>
    public string Source { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public Guid? PrinterId { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    public List<NotificationLineResponse> Lines { get; set; } = [];
}

public sealed class ListNotificationsResponse
{
    public List<NotificationItemResponse> Items { get; set; } = [];

    public int TotalCount { get; set; }
}

/// <summary>GET /api/v1/notifications — action-required items, newest first; the 50 newest, with the total count.</summary>
[Permission("print-jobs:view")]
public sealed class ListNotificationsEndpoint(IQueryHandler<ListNotificationsQuery, NotificationsPage> handler)
    : EndpointWithoutRequest<ListNotificationsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/notifications");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var page = await handler.HandleAsync(new ListNotificationsQuery(), ct);
        Response = new ListNotificationsResponse
        {
            TotalCount = page.TotalCount,
            Items = page.Items.Select(item => new NotificationItemResponse
            {
                Type = item.Type,
                PrintId = item.PrintId,
                Name = item.Name,
                Source = item.Source.ToString(),
                Status = item.Status.ToString(),
                PrinterId = item.PrinterId,
                EndedAtUtc = item.EndedAtUtc,
                Lines = item.Lines.Select(line => new NotificationLineResponse
                {
                    LineId = line.LineId,
                    ToolIndex = line.ToolIndex,
                    MaterialType = line.MaterialType,
                    ColorHex = line.ColorHex,
                    EstimatedGrams = line.EstimatedGrams,
                    SpoolId = line.SpoolId,
                    Reason = line.Reason.ToString(),
                }).ToList(),
            }).ToList(),
        };
    }
}
