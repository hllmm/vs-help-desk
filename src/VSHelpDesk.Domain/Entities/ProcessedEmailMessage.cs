namespace VSHelpDesk.Domain.Entities;

/// <summary>
/// Idempotency record for an inbound email Message-Id (UC-002).
/// Intentionally minimal — not a mailbox subsystem.
/// </summary>
public sealed class ProcessedEmailMessage
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string MessageId { get; private set; } = string.Empty;

    public DateTime ProcessedAt { get; private set; }

    public Guid? TicketId { get; private set; }

    private ProcessedEmailMessage()
    {
    }

    public ProcessedEmailMessage(string messageId, DateTime processedAt, Guid? ticketId = null)
    {
        MessageId = messageId;
        ProcessedAt = processedAt;
        TicketId = ticketId;
    }
}
