using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Domain.Entities;

using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Infrastructure.Persistence.Repositories;

public sealed class EfTicketRepository(IApplicationDbContext dbContext) : ITicketRepository
{
    public async Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Tickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default)
    {
        return await dbContext.Tickets.FirstOrDefaultAsync(t => t.TicketNumber == ticketNumber, cancellationToken);
    }

    public IQueryable<Ticket> GetListQueryable()
    {
        return dbContext.Tickets;
    }

    public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        dbContext.Add(ticket);
        return Task.CompletedTask;
    }

    public void Update(Ticket ticket)
    {
        if (dbContext is DbContext ef)
        {
            ef.Update(ticket);
        }
    }

    public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        dbContext.Add(message);
        return Task.CompletedTask;
    }

    public async Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return await dbContext.TicketMessages.AnyAsync(m => m.Id == messageId, cancellationToken);
    }

    public async Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return await dbContext.TicketMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
    }

    public async Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        return await dbContext.TicketMessages
            .Where(m => m.TicketId == ticketId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
