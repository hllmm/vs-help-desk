using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Application.Features.Tickets.ReadModel;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Infrastructure.Persistence.ReadModel;

public sealed class EfTicketListReadRepository(ApplicationDbContext applicationDbContext)
    : ITicketListReadRepository
{
    public async Task<TicketListReadResult> ReadAsync(
        TicketListReadRequest request,
        CancellationToken cancellationToken)
    {
        var searchedTickets = applicationDbContext.Tickets.AsNoTracking();

        if (!string.IsNullOrEmpty(request.Search))
        {
            var escapedSearch = EscapeLikePattern(request.Search);
            var pattern = $"%{escapedSearch}%";

            searchedTickets = searchedTickets.Where(ticket =>
                EF.Functions.ILike(ticket.TicketNumber, pattern, "\\") ||
                EF.Functions.ILike(ticket.Subject, pattern, "\\") ||
                EF.Functions.ILike(ticket.CustomerName, pattern, "\\") ||
                EF.Functions.ILike(ticket.CustomerEmail, pattern, "\\"));
        }

        var counts = await searchedTickets
            .GroupBy(_ => 1)
            .Select(group => new TicketStatusCountsDto(
                group.Count(),
                group.Count(ticket => ticket.Status == TicketStatus.New),
                group.Count(ticket => ticket.Status == TicketStatus.WaitingCustomerReply),
                group.Count(ticket => ticket.Status == TicketStatus.CustomerReplied),
                group.Count(ticket => ticket.Status == TicketStatus.Resolved)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new TicketStatusCountsDto(0, 0, 0, 0, 0);

        var pageQuery = searchedTickets;

        if (request.Status is { } status)
        {
            pageQuery = pageQuery.Where(ticket => ticket.Status == status);
        }

        if (request.Cursor is { } cursor)
        {
            pageQuery = pageQuery.Where(ticket =>
                ticket.LastActivityAt < cursor.LastActivityAt ||
                (ticket.LastActivityAt == cursor.LastActivityAt &&
                    string.Compare(ticket.TicketNumber, cursor.TicketNumber) > 0));
        }

        var items = await pageQuery
            .OrderByDescending(ticket => ticket.LastActivityAt)
            .ThenBy(ticket => ticket.TicketNumber)
            .Take(request.PageSize + 1)
            .Select(ticket => new TicketListItemDto(
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
                ticket.LastActivityAt,
                ticket.AssignedUserId))
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > request.PageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var nextCursor = hasMore && items.Count > 0
            ? new TicketListCursor(items[^1].LastActivityAt, items[^1].TicketNumber)
            : null;

        return new TicketListReadResult(items, nextCursor, hasMore, counts);
    }

    private static string EscapeLikePattern(string search) =>
        search
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
