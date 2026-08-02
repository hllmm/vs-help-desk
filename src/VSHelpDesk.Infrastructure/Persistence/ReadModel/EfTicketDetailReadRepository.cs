using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.ReadModel;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Infrastructure.Persistence.ReadModel;

public sealed class EfTicketDetailReadRepository(ApplicationDbContext applicationDbContext)
    : ITicketDetailReadRepository
{
    public async Task<TicketDetailsReadResult?> ReadDetailsAsync(
        Guid ticketId,
        int messagePageSize,
        CancellationToken cancellationToken)
    {
        var details = await applicationDbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .Select(ticket => new TicketDetailsDto(
                ticket.Id,
                ticket.TicketNumber,
                ticket.Subject,
                ticket.CustomerName,
                ticket.CustomerEmail,
                ticket.Status == TicketStatus.New
                    ? nameof(TicketStatus.New)
                    : ticket.Status == TicketStatus.WaitingCustomerReply
                        ? nameof(TicketStatus.WaitingCustomerReply)
                        : ticket.Status == TicketStatus.CustomerReplied
                            ? nameof(TicketStatus.CustomerReplied)
                            : nameof(TicketStatus.Resolved),
                ticket.AssignedUserId,
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.LastActivityAt,
                ticket.WaitingCustomerSince,
                ticket.ResolvedAt,
                ticket.ClosedByUserId,
                Array.Empty<TicketMessageDto>(),
                Array.Empty<TicketAttachmentMetaDto>(),
                null,
                false))
            .SingleOrDefaultAsync(cancellationToken);

        if (details is null)
        {
            return null;
        }

        var page = await ReadMessagePageAsync(
            ticketId,
            new TicketMessageReadRequest(messagePageSize, null),
            cancellationToken);

        return new TicketDetailsReadResult(
            details with
            {
                Messages = page.Messages,
                Attachments = page.Attachments
            },
            page.NextCursor,
            page.HasMore);
    }

    public async Task<TicketMessageReadResult?> ReadMessagesAsync(
        Guid ticketId,
        TicketMessageReadRequest request,
        CancellationToken cancellationToken)
    {
        var ticketExists = await applicationDbContext.Tickets
            .AsNoTracking()
            .AnyAsync(ticket => ticket.Id == ticketId, cancellationToken);

        if (!ticketExists)
        {
            return null;
        }

        return await ReadMessagePageAsync(ticketId, request, cancellationToken);
    }

    private async Task<TicketMessageReadResult> ReadMessagePageAsync(
        Guid ticketId,
        TicketMessageReadRequest request,
        CancellationToken cancellationToken)
    {
        var messageQuery = applicationDbContext.TicketMessages
            .AsNoTracking()
            .Where(message => message.TicketId == ticketId);

        if (request.Cursor is { } cursor)
        {
            messageQuery = messageQuery.Where(message =>
                message.CreatedAt < cursor.CreatedAt ||
                (message.CreatedAt == cursor.CreatedAt && message.Id.CompareTo(cursor.Id) < 0));
        }

        var messages = await messageQuery
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(request.PageSize + 1)
            .Select(message => new TicketMessageDto(
                message.Id,
                message.SenderType == MessageSenderType.Customer
                    ? nameof(MessageSenderType.Customer)
                    : message.SenderType == MessageSenderType.Support
                        ? nameof(MessageSenderType.Support)
                        : nameof(MessageSenderType.System),
                message.UserId,
                message.Content,
                message.IsHtml,
                message.CreatedAt))
            .ToListAsync(cancellationToken);

        var hasMore = messages.Count > request.PageSize;
        if (hasMore)
        {
            messages.RemoveAt(messages.Count - 1);
        }

        var nextCursor = hasMore && messages.Count > 0
            ? new TicketMessageCursor(messages[^1].CreatedAt, messages[^1].Id)
            : null;
        var messageIds = messages.Select(message => message.Id).ToArray();
        var attachments = messageIds.Length == 0
            ? []
            : await applicationDbContext.TicketAttachments
                .AsNoTracking()
                .Where(attachment => messageIds.Contains(attachment.TicketMessageId))
                .OrderByDescending(attachment => attachment.CreatedAt)
                .ThenByDescending(attachment => attachment.Id)
                .Select(attachment => new TicketAttachmentMetaDto(
                    attachment.Id,
                    attachment.TicketMessageId,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.FileSize,
                    attachment.CreatedAt))
                .ToListAsync(cancellationToken);

        messages.Reverse();
        attachments.Reverse();

        return new TicketMessageReadResult(messages, attachments, nextCursor, hasMore);
    }
}
