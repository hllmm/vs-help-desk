using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.Features.Tickets.GetTicketList;

public sealed record TicketListPageDto(
    IReadOnlyList<TicketListItemDto> Items,
    string? NextCursor,
    bool HasMore,
    TicketStatusCountsDto Counts);
