using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.CreateTicket;

public sealed record TicketDraft(
    Ticket Ticket,
    TicketMessage FirstMessage,
    DateTime CreatedAtUtc);

/// <summary>
/// Builds the shared ticket and first customer message graph used by inbound
/// email and portal creation. Idempotency state is deliberately owned by each
/// caller because the two sources have different contracts.
/// </summary>
public static class TicketDraftFactory
{
    public static async Task<TicketDraft> CreateAsync(
        ITicketNumberGenerator ticketNumberGenerator,
        TimeProvider timeProvider,
        string subject,
        string customerName,
        string customerEmail,
        string content,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ticketNumber = await ticketNumberGenerator.NextAsync(cancellationToken);
        var ticket = Ticket.Create(
            ticketNumber,
            subject,
            customerName,
            customerEmail,
            now);
        var firstMessage = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            content,
            isHtml: false,
            userId: null,
            createdAtUtc: now);

        ticket.RecordMessageActivity(now);
        return new TicketDraft(ticket, firstMessage, now);
    }
}
