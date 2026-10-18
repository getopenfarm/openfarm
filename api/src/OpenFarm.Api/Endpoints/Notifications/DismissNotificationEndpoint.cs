using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Production;
using DismissNotificationHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.DismissNotificationCommand, System.Guid>;

namespace OpenFarm.Api.Endpoints.Notifications;

public sealed class DismissNotificationRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "Print";

    public string EventType => "PrintFilamentFollowUpDismissed";
}

public sealed class DismissNotificationResponse
{
    public Guid Id { get; set; }
}

/// <summary>POST /api/v1/notifications/{id}/dismiss — {id} is the print's id; leaves its undeducted filament as is.</summary>
[Permission("print-jobs:update-status")]
public sealed class DismissNotificationEndpoint(DismissNotificationHandler handler)
    : Endpoint<DismissNotificationRequest, DismissNotificationResponse>
{
    public override void Configure()
    {
        Post("/api/v1/notifications/{id}/dismiss");
        Policies("Permission");
    }

    public override async Task HandleAsync(DismissNotificationRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        Response = new DismissNotificationResponse { Id = await handler.HandleAsync(new DismissNotificationCommand(req.Id), ct) };
    }
}
