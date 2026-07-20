using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Domain.Entities;

public sealed class TicketMessage
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid TicketId { get; private set; }

    public MessageSenderType SenderType { get; private set; }

    public Guid? UserId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public bool IsHtml { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private TicketMessage()
    {
    }

    public TicketMessage(
        Guid ticketId,
        MessageSenderType senderType,
        string content,
        bool isHtml = false,
        Guid? userId = null)
    {
        TicketId = ticketId;
        SenderType = senderType;
        Content = content;
        IsHtml = isHtml;
        UserId = userId;
    }
}
