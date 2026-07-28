using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();

    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();

    public DbSet<ProcessedEmailMessage> ProcessedEmailMessages => Set<ProcessedEmailMessage>();

    public DbSet<ApplicationParameter> ApplicationParameters => Set<ApplicationParameter>();

    public DbSet<ParameterChangeLog> ParameterChangeLogs => Set<ParameterChangeLog>();

    public DbSet<UserAdministrationAuditLog> UserAdministrationAuditLogs =>
        Set<UserAdministrationAuditLog>();

    IQueryable<User> IApplicationDbContext.Users => Users;

    IQueryable<Ticket> IApplicationDbContext.Tickets => Tickets;

    IQueryable<TicketMessage> IApplicationDbContext.TicketMessages => TicketMessages;

    IQueryable<TicketAttachment> IApplicationDbContext.TicketAttachments => TicketAttachments;

    IQueryable<ProcessedEmailMessage> IApplicationDbContext.ProcessedEmailMessages =>
        ProcessedEmailMessages;

    IQueryable<ApplicationParameter> IApplicationDbContext.ApplicationParameters =>
        ApplicationParameters;

    IQueryable<ParameterChangeLog> IApplicationDbContext.ParameterChangeLogs =>
        ParameterChangeLogs;

    void IApplicationDbContext.Add<TEntity>(TEntity entity) => Add(entity);

    void IApplicationDbContext.ClearTrackedChanges() => ChangeTracker.Clear();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OptimisticConcurrencyException(
                "The entity was modified by another process.",
                ex);
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OptimisticConcurrencyException(
                "The entity was modified by another process.",
                ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
