using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    IQueryable<User> Users { get; }

    IQueryable<Ticket> Tickets { get; }

    IQueryable<TicketMessage> TicketMessages { get; }

    IQueryable<TicketAttachment> TicketAttachments { get; }

    IQueryable<ProcessedEmailMessage> ProcessedEmailMessages { get; }

    IQueryable<PortalTicketRequest> PortalTicketRequests =>
        Enumerable.Empty<PortalTicketRequest>().AsQueryable();

    IQueryable<ApplicationParameter> ApplicationParameters { get; }

    IQueryable<ParameterChangeLog> ParameterChangeLogs { get; }

    IQueryable<SystemLog> SystemLogs { get; }

    IQueryable<UserAuditEvent> UserAuditEvents => Enumerable.Empty<UserAuditEvent>().AsQueryable();

    /// <summary>
    /// Indicates whether PostgreSQL-specific raw SQL is supported by this context.
    /// Callers must check this before requesting PostgreSQL-only statements.
    /// </summary>
    bool SupportsPostgresRawSql => false;

    void Add<TEntity>(TEntity entity) where TEntity : class;

    void Remove<TEntity>(TEntity entity) where TEntity : class { }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Drops tracked pending entities after a failed SaveChanges so the scoped context
    /// can continue processing later mails (idempotency race recovery).
    /// </summary>
    void ClearTrackedChanges();
}
