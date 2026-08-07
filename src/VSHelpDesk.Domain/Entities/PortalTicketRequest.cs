namespace VSHelpDesk.Domain.Entities;

/// <summary>
/// User-scoped idempotency state for portal-created tickets.
/// Stores only the normalized key and a hash of the normalized request.
/// </summary>
public sealed class PortalTicketRequest
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public Guid TicketId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private PortalTicketRequest()
    {
    }

    public static PortalTicketRequest Create(
        Guid userId,
        string idempotencyKey,
        string requestHash,
        Guid ticketId,
        DateTime createdAtUtc)
    {
        return new PortalTicketRequest
        {
            UserId = userId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            TicketId = ticketId,
            CreatedAtUtc = createdAtUtc
        };
    }
}
