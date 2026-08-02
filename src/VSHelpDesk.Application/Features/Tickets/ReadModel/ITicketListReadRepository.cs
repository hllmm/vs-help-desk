using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.ReadModel;

public sealed record TicketListReadRequest(
    TicketStatus? Status,
    string? Search,
    int PageSize,
    TicketListCursor? Cursor);

public sealed record TicketListReadResult(
    IReadOnlyList<TicketListItemDto> Items,
    TicketListCursor? NextCursor,
    bool HasMore,
    TicketStatusCountsDto Counts);

public interface ITicketListReadRepository
{
    Task<TicketListReadResult> ReadAsync(
        TicketListReadRequest request,
        CancellationToken cancellationToken);
}
