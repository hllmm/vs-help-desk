namespace VSHelpDesk.Domain.Entities;

public sealed class UserAdministrationAuditLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid ActorUserId { get; private set; }

    public Guid TargetUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public DateTime OccurredAt { get; private set; }

    public string? BeforeValue { get; private set; }

    public string? AfterValue { get; private set; }

    private UserAdministrationAuditLog()
    {
    }

    public UserAdministrationAuditLog(
        Guid actorUserId,
        Guid targetUserId,
        string action,
        DateTime occurredAtUtc,
        string? beforeValue = null,
        string? afterValue = null)
    {
        if (actorUserId == Guid.Empty || targetUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Actor and target user ids are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (action.Length > 64
            || beforeValue?.Length > 1000
            || afterValue?.Length > 1000)
        {
            throw new ArgumentException(
                "Audit value exceeds its maximum length.");
        }

        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        Action = action;
        OccurredAt = occurredAtUtc;
        BeforeValue = beforeValue;
        AfterValue = afterValue;
    }
}
