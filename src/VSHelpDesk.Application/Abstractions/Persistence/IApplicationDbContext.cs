using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    IQueryable<User> Users { get; }

    IQueryable<Ticket> Tickets { get; }

    IQueryable<TicketMessage> TicketMessages { get; }

    IQueryable<TicketAttachment> TicketAttachments { get; }

    IQueryable<ProcessedEmailMessage> ProcessedEmailMessages { get; }

    void Add<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
