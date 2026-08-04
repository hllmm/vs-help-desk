using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DbLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DbLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChannelWriter<SystemLog> _writer;
    private readonly LogLevel _minLogLevel;

    public DbLoggerProvider(ChannelWriter<SystemLog> writer, LogLevel minLogLevel = LogLevel.Error)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _minLogLevel = minLogLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DbLogger(name, _writer, _minLogLevel));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
