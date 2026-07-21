using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    IQueryable<User> Users { get; }

    IQueryable<Ticket> Tickets { get; }

    IQueryable<TicketMessage> TicketMessages { get; }

    IQueryable<TicketAttachment> TicketAttachments { get; }

    IQueryable<ProcessedEmailMessage> ProcessedEmailMessages { get; }

    IQueryable<ApplicationParameter> ApplicationParameters { get; }

    IQueryable<ParameterChangeLog> ParameterChangeLogs { get; }

    void Add<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops tracked pending entities after a failed SaveChanges so the scoped context
    /// can continue processing later mails (idempotency race recovery).
    /// </summary>
    void ClearTrackedChanges();
}
