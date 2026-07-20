using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Parameters;
using VSHelpDesk.Application.Features.Parameters.UpdateParameter;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.UnitTests.Features.Parameters;

public sealed class UpdateParameterHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Update_ToFive_PersistsAndReturnsNewValue()
    {
        var entity = new ApplicationParameter(
            ApplicationParameterCatalog.AutoResolveInactiveDaysKey,
            "3",
            "WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)");
        var db = new FakeDb(entity);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new UpdateParameterCommand(ApplicationParameterCatalog.AutoResolveInactiveDaysKey, "5"),
            CancellationToken.None);

        Assert.Equal(ApplicationParameterCatalog.AutoResolveInactiveDaysKey, result.Key);
        Assert.Equal("5", result.Value);
        Assert.Equal(entity.Description, result.Description);
        Assert.Equal(FixedNow.UtcDateTime, result.UpdatedAt);
        Assert.Equal("5", entity.Value);
        Assert.Equal(FixedNow.UtcDateTime, entity.UpdatedAt);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(1, db.ReaderEnsureCallCount);
    }

    [Fact]
    public async Task Update_Zero_ThrowsDomainException()
    {
        var entity = new ApplicationParameter(
            ApplicationParameterCatalog.AutoResolveInactiveDaysKey,
            "3",
            "desc");
        var db = new FakeDb(entity);
        var handler = CreateHandler(db);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new UpdateParameterCommand(ApplicationParameterCatalog.AutoResolveInactiveDaysKey, "0"),
                CancellationToken.None));

        Assert.Equal(ParameterCodes.ValueInvalid, ex.Message);
        Assert.Equal("3", entity.Value);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task Update_UnknownKey_ThrowsNotFoundException()
    {
        var db = new FakeDb();
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new UpdateParameterCommand("not.a.key", "1"),
                CancellationToken.None));

        Assert.Equal(0, db.SaveCallCount);
    }

    private static UpdateParameterHandler CreateHandler(FakeDb db) =>
        new(
            db,
            new CountingReader(db),
            new FixedTimeProvider(FixedNow));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CountingReader(FakeDb db) : IApplicationParameterReader
    {
        public Task EnsureCatalogAsync(CancellationToken cancellationToken = default)
        {
            db.ReaderEnsureCallCount++;
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
        public FakeDb(params ApplicationParameter[] parameters)
        {
            Parameters.AddRange(parameters);
        }

        public List<ApplicationParameter> Parameters { get; } = [];
        public int SaveCallCount { get; private set; }
        public int ReaderEnsureCallCount { get; set; }

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

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges()
        {
        }
    }
}
