using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence.Repositories;

public interface ITicketRepository
{
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default);

    IQueryable<Ticket> GetListQueryable();

    Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default);

    void Update(Ticket ticket);

    Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default);

    Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
