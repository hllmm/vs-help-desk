using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.Features.Tickets.GetTicketList;

public sealed class GetTicketListHandler(
    ITicketListReadRepository ticketListReadRepository,
    TicketListCursorCodec ticketListCursorCodec)
{
    public async Task<TicketListPageDto> HandleAsync(
        GetTicketListQuery query,
        CancellationToken cancellationToken = default)
    {
        var search = query.Search?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            search = null;
        }

        if (search is { Length: < 2 })
        {
            throw new RequestValidationException("ticket-search-too-short");
        }

        if (search is { Length: > 100 })
        {
            throw new RequestValidationException("ticket-search-too-long");
        }

        var cursor = query.Cursor is { Length: > 0 }
            ? ticketListCursorCodec.Decode(query.Cursor)
            : null;
        var result = await ticketListReadRepository.ReadAsync(
            new TicketListReadRequest(
                query.Status,
                search,
                Math.Clamp(query.PageSize, 1, 100),
                cursor),
            cancellationToken);

        return new TicketListPageDto(
            result.Items,
            result.NextCursor is null ? null : ticketListCursorCodec.Encode(result.NextCursor),
            result.HasMore,
            result.Counts);
    }
}
