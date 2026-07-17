using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FastEndpoints;
using OpenFarm.Application.ActivityLog;
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

        var organizationId = ExtractOrganizationId(httpContext.User);
        if (organizationId is null)
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
                    organizationId.Value, loggable.EntityType, entityId, loggable.EventType, actorId, actorType);
                await writer.WriteAsync(bulkEntry, ct);
            }

            return;
        }

        var singleEntityId = ExtractEntityId(context.Response) ?? ExtractEntityId(context.Request);
        if (singleEntityId is null)
        {
            return;
        }

        var entry = ActivityLogEntry.Create(
            organizationId.Value,
            loggable.EntityType,
            singleEntityId,
            loggable.EventType,
            actorId,
            actorType);

        await writer.WriteAsync(entry, ct);
    }

    private static string? ExtractEntityId(object? response) =>
        response?.GetType().GetProperty("Id")?.GetValue(response)?.ToString();

    private static Guid? ExtractOrganizationId(ClaimsPrincipal user)
    {
        var claim = user.FindFirst("org")?.Value;
        return Guid.TryParse(claim, out var organizationId) ? organizationId : null;
    }

    private static (Guid? ActorId, ActorType ActorType) ExtractActor(ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(sub, out var actorId) ? (actorId, ActorType.Operator) : (null, ActorType.System);
    }
}
