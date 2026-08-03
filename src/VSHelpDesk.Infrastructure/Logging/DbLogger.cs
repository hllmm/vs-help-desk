using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DbLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LogLevel _minLogLevel;

    public DbLogger(string categoryName, IServiceScopeFactory scopeFactory, LogLevel minLogLevel = LogLevel.Error)
    {
        _categoryName = categoryName ?? string.Empty;
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _minLogLevel = minLogLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _minLogLevel && logLevel != LogLevel.None;
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
            _categoryName.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var message = formatter != null ? formatter(state, exception) : state?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message) && exception == null)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetService<IApplicationDbContext>();
            if (dbContext == null)
            {
                return;
            }

            var logEntry = new SystemLog(
                logLevel: logLevel.ToString(),
                message: message,
                exception: exception?.ToString(),
                categoryName: _categoryName,
                eventId: eventId.Id != 0 ? eventId.Id : null,
                createdAt: DateTime.UtcNow);

            dbContext.Add(logEntry);
            dbContext.SaveChangesAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Fallback: Prevent logging failures from interrupting application execution
        }
    }
}
