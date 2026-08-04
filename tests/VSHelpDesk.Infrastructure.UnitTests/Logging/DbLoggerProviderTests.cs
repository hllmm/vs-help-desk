using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Logging;

namespace VSHelpDesk.Infrastructure.UnitTests.Logging;

public sealed class DbLoggerProviderTests
{
    private readonly Channel<SystemLog> _channel = Channel.CreateUnbounded<SystemLog>();
    private readonly IOptions<DatabaseLoggingOptions> _options = Options.Create(new DatabaseLoggingOptions());

    [Fact]
    public void CreateLogger_ReturnsLoggerInstance()
    {
        var provider = new DbLoggerProvider(_channel.Writer, _options);
        var logger = provider.CreateLogger("TestCategory");

        Assert.NotNull(logger);
    }

    [Fact]
    public void DbLogger_InformationLevel_DoesNotPersistToDb()
    {
        var provider = new DbLoggerProvider(_channel.Writer, _options);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("This should be ignored");

        Assert.False(_channel.Reader.TryRead(out _));
    }

    [Fact]
    public void DbLogger_ErrorAndCriticalLevel_PersistsSystemLogToDb()
    {
        var provider = new DbLoggerProvider(_channel.Writer, _options);
        var logger = provider.CreateLogger("TestCategory");
        var ex = new InvalidOperationException("Test exception");

        logger.LogError(ex, "An error occurred");
        logger.LogCritical("A critical error occurred");

        Assert.True(_channel.Reader.TryRead(out var errorLog));
        Assert.Equal("Error", errorLog.LogLevel);
        Assert.Equal("TestCategory", errorLog.CategoryName);
        Assert.Contains("An error occurred", errorLog.Message);
        Assert.NotNull(errorLog.Exception);
        Assert.Contains("Test exception", errorLog.Exception);

        Assert.True(_channel.Reader.TryRead(out var criticalLog));
        Assert.Equal("Critical", criticalLog.LogLevel);
        Assert.Contains("A critical error occurred", criticalLog.Message);
    }

    [Fact]
    public void DbLogger_EFCoreCategory_FiltersOutToPreventRecursion()
    {
        var provider = new DbLoggerProvider(_channel.Writer, _options);
        var logger = provider.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");

        logger.LogError("Database connection error");

        Assert.False(_channel.Reader.TryRead(out _));
    }
}
