using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DbLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DbLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChannelWriter<SystemLog> _writer;
    private readonly DatabaseLoggingOptions _options;
    private readonly ILogPropertySanitizer? _sanitizer;
    private readonly SystemLogDropMetrics? _dropMetrics;

    public DbLoggerProvider(
        ChannelWriter<SystemLog> writer,
        IOptions<DatabaseLoggingOptions> options,
        ILogPropertySanitizer? sanitizer = null,
        SystemLogDropMetrics? dropMetrics = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _options = options?.Value ?? new DatabaseLoggingOptions();
        _sanitizer = sanitizer;
        _dropMetrics = dropMetrics;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(
            categoryName,
            name => new DbLogger(name, _writer, _options, _sanitizer, _dropMetrics));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
