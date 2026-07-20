using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Parameters;
using VSHelpDesk.Application.Features.Parameters.GetParameters;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.UnitTests.Features.Parameters;

public sealed class GetParametersHandlerTests
{
    [Fact]
    public async Task List_EnsuresCatalog_AndReturnsAutoResolveInactiveDaysDefault()
    {
        var db = new FakeDb();
        var reader = new CatalogSeedingReader(db);
        var handler = new GetParametersHandler(db, reader);

        var items = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(1, reader.EnsureCallCount);
        Assert.Single(items);
        var item = items[0];
        Assert.Equal(ApplicationParameterCatalog.AutoResolveInactiveDaysKey, item.Key);
        Assert.Equal("3", item.Value);
        Assert.Equal(
            "WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)",
            item.Description);
    }

    private sealed class CatalogSeedingReader(FakeDb db) : IApplicationParameterReader
    {
        public int EnsureCallCount { get; private set; }

        public Task EnsureCatalogAsync(CancellationToken cancellationToken = default)
        {
            EnsureCallCount++;
            foreach (var definition in ApplicationParameterCatalog.All)
            {
                if (db.Parameters.Any(p => p.Key == definition.Key))
                {
                    continue;
                }

                db.Add(new ApplicationParameter(
                    definition.Key,
                    definition.DefaultValue,
                    definition.Description));
            }

            return Task.CompletedTask;
        }

        public Task<int> GetIntAsync(
            string key,
            int defaultValue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDb : IApplicationDbContext
    {
        public List<ApplicationParameter> Parameters { get; } = [];

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => Array.Empty<Ticket>().AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters => Parameters.AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
            if (entity is ApplicationParameter parameter)
            {
                Parameters.Add(parameter);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public void ClearTrackedChanges()
        {
        }
    }
}
