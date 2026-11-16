using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Application.AuditLog;

public sealed record AuditContext(Guid? ActorId, string? ActorName, ActivityChannel? Via,
    string? Operation, string CorrelationId, string? EntityType = null, string? EventType = null, Guid? InitiatedByUserId = null);

public interface IAuditContextAccessor
{
    AuditContext? Current { get; }
}
