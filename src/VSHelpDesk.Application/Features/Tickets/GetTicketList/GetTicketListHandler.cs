using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.GetTicketList;

public sealed class GetTicketListHandler(IApplicationDbContext applicationDbContext)
{
    public Task<IReadOnlyList<TicketListItemDto>> HandleAsync(
        GetTicketListQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        IQueryable<Domain.Entities.Ticket> tickets = applicationDbContext.Tickets;

        if (query.Status is { } status)
        {
            tickets = tickets.Where(ticket => ticket.Status == status);
        }

        // Projection-only: no full entity materialization for list (BR-014 consumers get DTO only).
        var items = tickets
            .OrderByDescending(ticket => ticket.LastActivityAt)
            .ThenBy(ticket => ticket.TicketNumber)
            .Select(ticket => new TicketListItemDto(
                ticket.Id,
                ticket.TicketNumber,
                ticket.Subject,
                ticket.CustomerName,
                ticket.CustomerEmail,
                ticket.Status.ToString(),
                ticket.LastActivityAt,
                ticket.AssignedUserId))
            .ToList();

        return Task.FromResult<IReadOnlyList<TicketListItemDto>>(items);
    }
}
