using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Logging;

namespace VSHelpDesk.Infrastructure.UnitTests.Logging;

public sealed class DbLoggerProviderTests
{
    private readonly TestDbContext _dbContext = new();

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationDbContext>(_dbContext);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void CreateLogger_ReturnsLoggerInstance()
    {
        var provider = new DbLoggerProvider(CreateScopeFactory());
        var logger = provider.CreateLogger("TestCategory");

        Assert.NotNull(logger);
    }

    [Fact]
    public void DbLogger_InformationLevel_DoesNotPersistToDb()
    {
        var provider = new DbLoggerProvider(CreateScopeFactory());
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("This should be ignored");

        Assert.Empty(_dbContext.Logs);
    }

    [Fact]
    public void DbLogger_ErrorAndCriticalLevel_PersistsSystemLogToDb()
    {
        var provider = new DbLoggerProvider(CreateScopeFactory());
        var logger = provider.CreateLogger("TestCategory");
        var ex = new InvalidOperationException("Test exception");

        logger.LogError(ex, "An error occurred");
        logger.LogCritical("A critical error occurred");

        Assert.Equal(2, _dbContext.Logs.Count);

        var errorLog = _dbContext.Logs[0];
        Assert.Equal("Error", errorLog.LogLevel);
        Assert.Equal("TestCategory", errorLog.CategoryName);
        Assert.Contains("An error occurred", errorLog.Message);
        Assert.NotNull(errorLog.Exception);
        Assert.Contains("Test exception", errorLog.Exception);

        var criticalLog = _dbContext.Logs[1];
        Assert.Equal("Critical", criticalLog.LogLevel);
        Assert.Contains("A critical error occurred", criticalLog.Message);
    }

    [Fact]
    public void DbLogger_EFCoreCategory_FiltersOutToPreventRecursion()
    {
        var provider = new DbLoggerProvider(CreateScopeFactory());
        var logger = provider.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");

        logger.LogError("Database connection error");

        Assert.Empty(_dbContext.Logs);
    }

    private sealed class TestDbContext : IApplicationDbContext
    {
        public List<SystemLog> Logs { get; } = [];

        public IQueryable<User> Users => Enumerable.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => Enumerable.Empty<Ticket>().AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Enumerable.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => Enumerable.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages => Enumerable.Empty<ProcessedEmailMessage>().AsQueryable();
        public IQueryable<ApplicationParameter> ApplicationParameters => Enumerable.Empty<ApplicationParameter>().AsQueryable();
        public IQueryable<ParameterChangeLog> ParameterChangeLogs => Enumerable.Empty<ParameterChangeLog>().AsQueryable();
        public IQueryable<SystemLog> SystemLogs => Logs.AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
            if (entity is SystemLog log)
            {
                Logs.Add(log);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => Logs.Clear();
    }
}
