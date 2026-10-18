using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FastEndpoints;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Api.Processors;

public sealed class ActivityLogPostProcessor : IGlobalPostProcessor
{
    public async Task PostProcessAsync(IPostProcessorContext context, CancellationToken ct)
    {
        if (context.HasExceptionOccurred || context.HasValidationFailures)
        {
            return;
        }

        var httpContext = context.HttpContext;

        if (httpContext.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        if (context.Request is not IActivityLoggable loggable)
        {
            return;
        }

        var (actorId, actorType) = ExtractActor(httpContext.User);
        var writer = httpContext.RequestServices.GetRequiredService<IActivityLogWriter>();

        if (context.Response is IHasEntityIds hasEntityIds)
        {
            foreach (var entityId in hasEntityIds.EntityIds)
            {
                var bulkEntry = ActivityLogEntry.Create(
                    loggable.EntityType, entityId, loggable.EventType, actorId, actorType);
                await writer.WriteAsync(bulkEntry, ct);
                await NotifyPrinterActivityAsync(httpContext, bulkEntry, ct);
            }

            return;
        }

        var singleEntityId = ExtractEntityId(context.Response) ?? ExtractEntityId(context.Request);
        if (singleEntityId is null)
        {
            return;
        }

        var entry = ActivityLogEntry.Create(
            loggable.EntityType,
            singleEntityId,
            loggable.EventType,
            actorId,
            actorType);

        await writer.WriteAsync(entry, ct);
        await NotifyPrinterActivityAsync(httpContext, entry, ct);
    }

    private static Task NotifyPrinterActivityAsync(HttpContext context, ActivityLogEntry entry, CancellationToken ct) =>
        entry.EntityType == "Printer" && Guid.TryParse(entry.EntityId, out var printerId)
            ? context.RequestServices.GetRequiredService<IPrinterEventPublisher>()
                .PrinterActivityChangedAsync(printerId, ct)
            : Task.CompletedTask;

    private static string? ExtractEntityId(object? response) =>
        response?.GetType().GetProperty("Id")?.GetValue(response)?.ToString();

    private static (Guid? ActorId, ActorType ActorType) ExtractActor(ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(sub, out var actorId) ? (actorId, ActorType.Operator) : (null, ActorType.System);
    }
}
