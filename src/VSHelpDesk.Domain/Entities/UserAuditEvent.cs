namespace VSHelpDesk.Domain.Entities;

/// <summary>
/// Append-only audit row for privileged user-admin operations.
/// Never stores passwords or hashes.
/// </summary>
public sealed class UserAuditEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid ActorUserId { get; private set; }

    public Guid TargetUserId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string? BeforeRole { get; private set; }

    public string? AfterRole { get; private set; }

    public bool? BeforeIsActive { get; private set; }

    public bool? AfterIsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public string? CorrelationId { get; private set; }

    private UserAuditEvent()
    {
    }

    public UserAuditEvent(
        Guid actorUserId,
        Guid targetUserId,
        string eventType,
        string? beforeRole,
        string? afterRole,
        bool? beforeIsActive,
        bool? afterIsActive,
        DateTime createdAtUtc,
        string? correlationId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        if (targetUserId == Guid.Empty)
        {
            throw new ArgumentException("Target user id is required.", nameof(targetUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var normalized = eventType.Trim();
        if (normalized != "Created" && normalized != "RoleChanged" && normalized != "ActiveChanged" && normalized != "PasswordReset")
        {
            throw new ArgumentException($"Invalid event type '{eventType}'.", nameof(eventType));
        }

        Id = Guid.NewGuid();
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        EventType = normalized;
        BeforeRole = beforeRole;
        AfterRole = afterRole;
        BeforeIsActive = beforeIsActive;
        AfterIsActive = afterIsActive;
        CreatedAt = createdAtUtc.Kind == DateTimeKind.Utc ? createdAtUtc : createdAtUtc.ToUniversalTime();
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
    }
}
