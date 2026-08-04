using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence;

/// <summary>
/// Abstraction for database-provider-specific ticket text searching strategies.
/// </summary>
public interface ITicketSearchStrategy
{
    IQueryable<Ticket> ApplySearchFilter(IQueryable<Ticket> query, string searchTerm);
}
