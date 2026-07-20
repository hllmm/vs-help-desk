using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();

    public DbSet<ProcessedEmailMessage> ProcessedEmailMessages => Set<ProcessedEmailMessage>();

    IQueryable<User> IApplicationDbContext.Users => Users;

    IQueryable<Ticket> IApplicationDbContext.Tickets => Tickets;

    IQueryable<TicketMessage> IApplicationDbContext.TicketMessages => TicketMessages;

    IQueryable<ProcessedEmailMessage> IApplicationDbContext.ProcessedEmailMessages =>
        ProcessedEmailMessages;

    void IApplicationDbContext.Add<TEntity>(TEntity entity) => Add(entity);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
