using OpenFarm.Application.AuditLog;

namespace OpenFarm.Api.Security;

public sealed class HttpAuditContextAccessor(IHttpContextAccessor http) : IAuditContextAccessor
{
    public AuditContext? Current
    {
        get
        {
            if (AuditContextScope.Current is { } execution) return execution;
            var context = http.HttpContext;
            if (context?.User.Identity?.IsAuthenticated != true) return null;
            var actor = context.User.FindFirst("sub")?.Value;
            var operation = context.Items["AuditOperation"] as string
                ?? $"{context.Request.Method} {context.Request.Path}";
            return new(Guid.TryParse(actor, out var id) ? id : null, null,
                CredentialClaims.Channel(context.User), operation,
                context.Items["AuditCorrelationId"] as string ?? context.TraceIdentifier,
                context.Items["AuditEntityType"] as string, context.Items["AuditEventType"] as string);
        }
    }
}
