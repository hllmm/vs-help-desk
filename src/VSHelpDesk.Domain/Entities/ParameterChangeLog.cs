namespace VSHelpDesk.Domain.Entities;

/// <summary>
/// Audit row for a successful application parameter update (who/when/old/new).
/// </summary>
public sealed class ParameterChangeLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string ParameterKey { get; private set; } = string.Empty;

    public string OldValue { get; private set; } = string.Empty;

    public string NewValue { get; private set; } = string.Empty;

    public Guid ChangedByUserId { get; private set; }

    public DateTime ChangedAt { get; private set; }

    private ParameterChangeLog()
    {
    }

    public ParameterChangeLog(
        string parameterKey,
        string oldValue,
        string newValue,
        Guid changedByUserId,
        DateTime changedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterKey);
        ArgumentNullException.ThrowIfNull(oldValue);
        ArgumentNullException.ThrowIfNull(newValue);
        if (changedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Changed-by user id is required.", nameof(changedByUserId));
        }

        ParameterKey = parameterKey.Trim();
        OldValue = oldValue;
        NewValue = newValue;
        ChangedByUserId = changedByUserId;
        ChangedAt = changedAtUtc;
    }
}
