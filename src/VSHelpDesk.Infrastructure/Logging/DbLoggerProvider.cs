using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DbLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DbLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LogLevel _minLogLevel;

    public DbLoggerProvider(IServiceScopeFactory scopeFactory, LogLevel minLogLevel = LogLevel.Error)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _minLogLevel = minLogLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DbLogger(name, _scopeFactory, _minLogLevel));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
