using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.Features.Tickets.GetTicketMessages;

public sealed class GetTicketMessagesHandler(
    ITicketDetailReadRepository ticketDetailReadRepository,
    TicketMessageCursorCodec ticketMessageCursorCodec,
    IMessageProvider? messages = null)
{
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;

    public async Task<TicketMessagePageDto> HandleAsync(
        GetTicketMessagesQuery query,
        CancellationToken cancellationToken = default)
    {
        var cursor = query.Cursor is not null
            ? ticketMessageCursorCodec.Decode(query.Cursor)
            : null;
        var result = await ticketDetailReadRepository.ReadMessagesAsync(
            query.TicketId,
            new TicketMessageReadRequest(
                Math.Clamp(query.PageSize, 1, 200),
                cursor),
            cancellationToken);

        if (result is null)
        {
            throw new NotFoundException(_messages.Get(MessageKeys.Tickets.NotFound, query.TicketId));
        }

        return new TicketMessagePageDto(
            result.Messages,
            result.Attachments,
            result.NextCursor is null
                ? null
                : ticketMessageCursorCodec.Encode(result.NextCursor),
            result.HasMore);
    }
}
