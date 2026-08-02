namespace VSHelpDesk.Application.Features.Tickets.GetTicketList;

public sealed record TicketStatusCountsDto(
    int All,
    int New,
    int WaitingCustomerReply,
    int CustomerReplied,
    int Resolved);
