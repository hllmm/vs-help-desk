using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DbLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ChannelWriter<SystemLog> _writer;
    private readonly DatabaseLoggingOptions _options;
    private readonly ILogPropertySanitizer? _sanitizer;
    private readonly SystemLogDropMetrics? _dropMetrics;

    public DbLogger(
        string categoryName,
        ChannelWriter<SystemLog> writer,
        DatabaseLoggingOptions options,
        ILogPropertySanitizer? sanitizer = null,
        SystemLogDropMetrics? dropMetrics = null)
    {
        _categoryName = categoryName ?? string.Empty;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _options = options ?? new DatabaseLoggingOptions();
        _sanitizer = sanitizer;
        _dropMetrics = dropMetrics;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _options.MinimumLevel && logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Avoid infinite recursion loops if EF Core or database infrastructure emits error logs
        if (_categoryName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
            _categoryName.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            _categoryName.StartsWith("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var message = formatter != null ? formatter(state, exception) : state?.ToString() ?? string.Empty;
        var exceptionString = exception?.ToString();

        if (_options.SanitizePII && _sanitizer != null)
        {
            message = _sanitizer.Sanitize(message) ?? message;
            exceptionString = _sanitizer.Sanitize(exceptionString);
        }

        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(exceptionString))
        {
            return;
        }

        try
        {
            var logEntry = new SystemLog(
                logLevel: logLevel.ToString(),
                message: message,
                exception: exceptionString,
                categoryName: _categoryName,
                eventId: eventId.Id != 0 ? eventId.Id : null,
                createdAt: DateTime.UtcNow);

            if (!_writer.TryWrite(logEntry))
            {
                _dropMetrics?.IncrementDroppedCount();
            }
        }
        catch
        {
            _dropMetrics?.IncrementDroppedCount();
        }
    }
}
