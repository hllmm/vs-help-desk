using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Application.Features.Tickets.GetTicketDetails;

public sealed class GetTicketDetailsHandler(IApplicationDbContext applicationDbContext)
{
    public Task<TicketDetailsDto> HandleAsync(
        GetTicketDetailsQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var ticket = applicationDbContext.Tickets
            .Where(candidate => candidate.Id == query.TicketId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.TicketNumber,
                candidate.Subject,
                candidate.CustomerName,
                candidate.CustomerEmail,
                Status = candidate.Status.ToString(),
                candidate.AssignedUserId,
                candidate.CreatedAt,
                candidate.UpdatedAt,
                candidate.LastActivityAt,
                candidate.WaitingCustomerSince,
                candidate.ResolvedAt,
                candidate.ClosedByUserId
            })
            .FirstOrDefault();

        if (ticket is null)
        {
            throw new NotFoundException($"Ticket '{query.TicketId}' was not found.");
        }

        // BR-020: chronological messages; secondary key for deterministic order.
        var messages = applicationDbContext.TicketMessages
            .Where(message => message.TicketId == query.TicketId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Select(message => new TicketMessageDto(
                message.Id,
                message.SenderType.ToString(),
                message.UserId,
                message.Content,
                message.IsHtml,
                message.CreatedAt))
            .ToList();

        // Attachment table not mapped yet — return empty metadata (schema later).
        IReadOnlyList<TicketAttachmentMetaDto> attachments = Array.Empty<TicketAttachmentMetaDto>();

        var details = new TicketDetailsDto(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.CustomerName,
            ticket.CustomerEmail,
            ticket.Status,
            ticket.AssignedUserId,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.LastActivityAt,
            ticket.WaitingCustomerSince,
            ticket.ResolvedAt,
            ticket.ClosedByUserId,
            messages,
            attachments);

        return Task.FromResult(details);
    }
}
