namespace OpenFarm.Domain.ActivityLog;

public sealed class ActivityLogEntry
{
    private ActivityLogEntry(
        Guid id,
        Guid organizationId,
        string entityType,
        string entityId,
        string eventType,
        Guid? actorId,
        ActorType actorType,
        DateTimeOffset timestampUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        EntityType = entityType;
        EntityId = entityId;
        EventType = eventType;
        ActorId = actorId;
        ActorType = actorType;
        TimestampUtc = timestampUtc;
    }

    public ActivityLogDetails? Details { get; private set; }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public string EntityType { get; }

    public string EntityId { get; }

    public string EventType { get; }

    public Guid? ActorId { get; }

    public ActorType ActorType { get; }

    public DateTimeOffset TimestampUtc { get; }

    public static ActivityLogEntry Create(
        Guid organizationId,
        string entityType,
        string entityId,
        string eventType,
        Guid? actorId,
        ActorType actorType,
        ActivityLogDetails? details = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type is required.", nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("Entity id is required.", nameof(entityId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type is required.", nameof(eventType));
        }

        if (actorType == ActorType.Operator && actorId is null)
        {
            throw new ArgumentException("Operator-attributed entries require an actor id.", nameof(actorId));
        }

        return new ActivityLogEntry(
            Guid.NewGuid(),
            organizationId,
            entityType,
            entityId,
            eventType,
            actorId,
            actorType,
            DateTimeOffset.UtcNow) { Details = details };
    }
}
