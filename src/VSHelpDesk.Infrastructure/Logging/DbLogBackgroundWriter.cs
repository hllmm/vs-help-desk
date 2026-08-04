
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DbLogBackgroundWriter : BackgroundService
{
    private readonly ChannelReader<SystemLog> _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DatabaseLoggingOptions _options;
    private readonly SystemLogDropMetrics _dropMetrics;

    public DbLogBackgroundWriter(
        ChannelReader<SystemLog> reader,
        IServiceScopeFactory scopeFactory,
        IOptions<DatabaseLoggingOptions> options,
        SystemLogDropMetrics dropMetrics)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _dropMetrics = dropMetrics ?? throw new ArgumentNullException(nameof(dropMetrics));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SystemLog>(Math.Clamp(_options.BatchSize, 1, 1000));
        try
        {
            await foreach (var log in _reader.ReadAllAsync(stoppingToken))
            {
                batch.Add(log);
                if (batch.Count >= _options.BatchSize || _reader.Count == 0)
                {
                    await PersistAndClearAsync(batch, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is stopping. Flush below with a short independent token.
        }
        catch (Exception exception)
        {
            await SafeWriteErrorAsync($"[DbLogBackgroundWriter] Unexpected reader failure: {exception.Message}");
        }
        finally
        {
            if (batch.Count > 0)
            {
                using var flushTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await PersistAndClearAsync(batch, flushTimeout.Token);
            }
        }
    }

    private async Task PersistAndClearAsync(List<SystemLog> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        var attempts = Math.Clamp(_options.MaxWriteAttempts, 1, 10);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                foreach (var entry in batch) dbContext.Add(entry);
                await dbContext.SaveChangesAsync(cancellationToken);
                batch.Clear();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lastError = null;
                break;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt < attempts)
                {
                    var multiplier = 1 << Math.Min(attempt - 1, 6);
                    var delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMilliseconds * multiplier);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        _dropMetrics.IncrementDroppedCount(batch.Count);
        await SafeWriteErrorAsync(
            $"[DbLogBackgroundWriter] Permanently dropped {batch.Count} log entries after {attempts} attempts: {lastError?.Message ?? "shutdown timeout"}");
        batch.Clear();
    }

    private static async Task SafeWriteErrorAsync(string message)
    {
        try { await Console.Error.WriteLineAsync(message); }
        catch { /* Logging must never terminate the host. */ }
    }
}
