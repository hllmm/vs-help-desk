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

    /// <summary>
    /// Drops tracked pending entities after a failed SaveChanges so the scoped context
    /// can continue processing later mails (MessageId race recovery).
    /// </summary>
    void ClearTrackedChanges();

    /// <summary>
    /// True when the exception is a unique-constraint / concurrency conflict that may
    /// indicate a racing MessageId insert (PostgreSQL 23505 / EF DbUpdateException).
    /// </summary>
    bool IsUniqueConstraintViolation(Exception exception);
}
