
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DbLogRetentionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DatabaseLoggingOptions _options;
    private readonly ILogger<DbLogRetentionHostedService> _logger;
    private readonly TimeSpan _checkInterval;

    public DbLogRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DatabaseLoggingOptions> options,
        ILogger<DbLogRetentionHostedService> logger,
        TimeSpan? checkInterval = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _checkInterval = checkInterval ?? TimeSpan.FromHours(24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_checkInterval);
        do
        {
            try { await PurgeExpiredLogsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "An error occurred while purging expired database logs."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task PurgeExpiredLogsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Abs(_options.RetentionDays));
        var batchSize = Math.Clamp(_options.RetentionBatchSize, 100, 5000);
        var totalDeleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var expiredLogs = await dbContext.SystemLogs
                .Where(log => log.CreatedAt < cutoff)
                .OrderBy(log => log.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (expiredLogs.Count == 0) break;
            foreach (var log in expiredLogs) dbContext.Remove(log);
            await dbContext.SaveChangesAsync(cancellationToken);
            totalDeleted += expiredLogs.Count;
            if (expiredLogs.Count < batchSize) break;
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Purged {Count} expired system log entries older than {Cutoff}.",
                totalDeleted,
                cutoff);
        }
    }
}
