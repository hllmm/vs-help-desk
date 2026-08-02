using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.Features.Tickets.GetTicketDetails;

public sealed class GetTicketDetailsHandler(
    ITicketDetailReadRepository ticketDetailReadRepository,
    TicketMessageCursorCodec ticketMessageCursorCodec)
{
    private const int InitialMessagePageSize = 100;

    public async Task<TicketDetailsDto> HandleAsync(
        GetTicketDetailsQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = await ticketDetailReadRepository.ReadDetailsAsync(
            query.TicketId,
            InitialMessagePageSize,
            cancellationToken);

        if (result is null)
        {
            throw new NotFoundException($"Ticket '{query.TicketId}' was not found.");
        }

        return result.Details with
        {
            NextMessageCursor = result.NextCursor is null
                ? null
                : ticketMessageCursorCodec.Encode(result.NextCursor),
            HasMoreMessages = result.HasMoreMessages
        };
    }
}
