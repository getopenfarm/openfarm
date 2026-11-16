using System.Text.Json;

namespace OpenFarm.Domain.ActivityLog;

public sealed class ActivityLogEntry
{
    private ActivityLogEntry(
        Guid id,
        string entityType,
        string entityId,
        string eventType,
        Guid? actorId,
        ActorType actorType,
        DateTimeOffset timestampUtc)
    {
        Id = id;
        EntityType = entityType;
        EntityId = entityId;
        EventType = eventType;
        ActorId = actorId;
        ActorType = actorType;
        TimestampUtc = timestampUtc;
    }

    public Guid[] RelatedPrinterIds { get; private set; } = [];
    public void RelatePrinters(IEnumerable<Guid> ids) => RelatedPrinterIds = RelatedPrinterIds.Concat(ids).Distinct().ToArray();

    public void AttributeInitiator(Guid? userId, string? name = null)
    {
        if (userId is not null) Details = (Details ?? new ActivityLogDetails()) with { InitiatedByUserId = userId, InitiatedByName = name ?? Details?.InitiatedByName };
    }

    public string Category { get; private set; } = "Audit";
    public string? Action { get; private set; }
    public string? EntityLabel { get; private set; }
    public string? ActorName { get; private set; }
    public string? Operation { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? Channel { get; private set; }
    public Guid? CredentialId { get; private set; }
    public string? CredentialLabel { get; private set; }
    public string? ChangesJson { get; private set; }

    public void Describe(string action, string? label, string? actorName, string? operation,
        string? correlationId, string channel, IReadOnlyList<AuditFieldChange>? changes = null)
    {
        Action ??= action;
        EntityLabel ??= label;
        ActorName ??= actorName;
        Operation ??= operation;
        CorrelationId ??= correlationId;
        Channel ??= channel;
        if (changes is { Count: > 0 }) RecordChanges(action, label, changes);
    }

    public void RecordChanges(string action, string? label, IReadOnlyList<AuditFieldChange> changes)
    {
        Action ??= action;
        EntityLabel ??= label;
        if (changes.Count > 0) ChangesJson = JsonSerializer.Serialize(changes.Select(c =>
            new AuditFieldChange(c.Field, AuditFieldValue.Sanitize(c.Field, c.Before), AuditFieldValue.Sanitize(c.Field, c.After))));
    }

    public ActivityLogDetails? Details { get; private set; }

    public Guid Id { get; }

    public string EntityType { get; }

    public string EntityId { get; }

    public string EventType { get; }

    public Guid? ActorId { get; }

    public ActorType ActorType { get; }

    public DateTimeOffset TimestampUtc { get; }

    /// <summary>Records the token or app an operator used. The first attribution wins.</summary>
    public void AttributeTo(ActivityChannel via)
    {
        ArgumentNullException.ThrowIfNull(via);
        if (ActorType != ActorType.Operator || Details?.Via is not null) return;
        CredentialId = via.CredentialId;
        CredentialLabel = via.Label;
        Details = (Details ?? new ActivityLogDetails()) with { Via = via };
    }

    public static ActivityLogEntry Create(
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
            entityType,
            entityId,
            eventType,
            actorId,
            actorType,
            DateTimeOffset.UtcNow)
        {
            Details = details is null ? null : details with
            {
                PreviousState = AuditFieldValue.SanitizeSnapshot(details.PreviousState),
                ReportedState = AuditFieldValue.SanitizeSnapshot(details.ReportedState),
            },
            Category = eventType is "PrinterStatusObserved" or "PrinterAlertsChanged" ? "MachineObservation" : "Audit",
        };
    }
}

public sealed record AuditFieldChange(string Field, object? Before, object? After);
