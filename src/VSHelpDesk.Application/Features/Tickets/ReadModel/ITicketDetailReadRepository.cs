using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;

namespace VSHelpDesk.Application.Features.Tickets.ReadModel;

public sealed record TicketMessageReadRequest(
    int PageSize,
    TicketMessageCursor? Cursor);

public sealed record TicketMessageReadResult(
    IReadOnlyList<TicketMessageDto> Messages,
    IReadOnlyList<TicketAttachmentMetaDto> Attachments,
    TicketMessageCursor? NextCursor,
    bool HasMore);

public sealed record TicketDetailsReadResult(
    TicketDetailsDto Details,
    TicketMessageCursor? NextCursor,
    bool HasMoreMessages);

public interface ITicketDetailReadRepository
{
    Task<TicketDetailsReadResult?> ReadDetailsAsync(
        Guid ticketId,
        int messagePageSize,
        CancellationToken cancellationToken);

    Task<TicketMessageReadResult?> ReadMessagesAsync(
        Guid ticketId,
        TicketMessageReadRequest request,
        CancellationToken cancellationToken);
}
