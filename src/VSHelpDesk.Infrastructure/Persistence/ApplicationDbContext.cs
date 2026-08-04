using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Persistence.Configurations;

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

    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();

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

    IQueryable<SystemLog> IApplicationDbContext.SystemLogs =>
        SystemLogs;

    void IApplicationDbContext.Add<TEntity>(TEntity entity) => Add(entity);

    void IApplicationDbContext.Remove<TEntity>(TEntity entity) => Remove(entity);

    void IApplicationDbContext.ClearTrackedChanges() => ChangeTracker.Clear();

    public async Task ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (Database.IsNpgsql())
        {
            await Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

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
        var isPostgres = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        if (isPostgres)
        {
            modelBuilder.HasPostgresExtension("pg_trgm");
        }

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly,
            configurationType => configurationType != typeof(TicketConfiguration));
        modelBuilder.ApplyConfiguration(new TicketConfiguration(isPostgres));
        base.OnModelCreating(modelBuilder);
    }
}
