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

    public DbSet<UserAuditEvent> UserAuditEvents => Set<UserAuditEvent>();

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

    IQueryable<UserAuditEvent> IApplicationDbContext.UserAuditEvents => UserAuditEvents;

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
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly,
            configurationType => configurationType != typeof(TicketConfiguration));
        modelBuilder.ApplyConfiguration(new TicketConfiguration(true));

        // Ensure column types match the single migration snapshot for all providers
        // (Postgres, Sqlite, InMemory) so that PendingModelChangesWarning does not trigger.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var clrType = property.ClrType;
                var maxLength = property.GetMaxLength();

                if (clrType == typeof(Guid))
                {
                    property.SetColumnType("uuid");
                }
                else if (clrType == typeof(string))
                {
                    if (maxLength.HasValue)
                    {
                        property.SetColumnType($"character varying({maxLength.Value})");
                    }
                    else
                    {
                        property.SetColumnType("text");
                    }
                }
                else if (clrType == typeof(DateTime) || clrType == typeof(DateTime?))
                {
                    property.SetColumnType("timestamp with time zone");
                }
                else if (clrType == typeof(bool) || clrType == typeof(bool?))
                {
                    property.SetColumnType("boolean");
                }
                else if (clrType == typeof(int) || clrType == typeof(int?) || clrType.IsEnum || (Nullable.GetUnderlyingType(clrType)?.IsEnum == true))
                {
                    property.SetColumnType("integer");
                }
                else if (clrType == typeof(long) || clrType == typeof(long?))
                {
                    property.SetColumnType("bigint");
                }
                else if (clrType == typeof(uint) || clrType == typeof(uint?))
                {
                    property.SetColumnType("xid");
                    property.SetColumnName("xmin");
                    property.IsConcurrencyToken = true;
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
                }
            }

            var versionProperty = entityType.FindProperty("Version");
            if (versionProperty != null && versionProperty.ClrType == typeof(uint))
            {
                versionProperty.SetColumnType("xid");
                versionProperty.SetColumnName("xmin");
                versionProperty.IsConcurrencyToken = true;
                versionProperty.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
