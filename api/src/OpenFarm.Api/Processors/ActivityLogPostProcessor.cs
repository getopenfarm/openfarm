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

        var entityId = ExtractEntityId(context.Response) ?? ExtractEntityId(context.Request);
        var organizationId = ExtractOrganizationId(httpContext.User);

        if (entityId is null || organizationId is null)
        {
            return;
        }

        var (actorId, actorType) = ExtractActor(httpContext.User);

        var entry = ActivityLogEntry.Create(
            organizationId.Value,
            loggable.EntityType,
            entityId,
            loggable.EventType,
            actorId,
            actorType);

        var writer = httpContext.RequestServices.GetRequiredService<IActivityLogWriter>();
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
