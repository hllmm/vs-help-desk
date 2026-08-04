using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Logging;

public class DbLogBackgroundWriter : BackgroundService
{
    private readonly ChannelReader<SystemLog> _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DatabaseLoggingOptions _options;

    public DbLogBackgroundWriter(
        ChannelReader<SystemLog> reader,
        IServiceScopeFactory scopeFactory,
        IOptions<DatabaseLoggingOptions> options)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? new DatabaseLoggingOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchSize = Math.Clamp(_options.BatchSize, 1, 1000);
        var batch = new List<SystemLog>(batchSize);

        try
        {
            await foreach (var log in _reader.ReadAllAsync(stoppingToken))
            {
                batch.Add(log);

                if (batch.Count >= batchSize || _reader.Count == 0)
                {
                    await WriteBatchAsync(batch, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore when stopping
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbLogBackgroundWriter] Unexpected error reading logs: {ex.Message}");
        }
    }

    private async Task WriteBatchAsync(List<SystemLog> batch, CancellationToken stoppingToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            foreach (var logEntry in batch)
            {
                dbContext.Add(logEntry);
            }
            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            // Fallback: safe stderr output so failures are observable
            try
            {
                await Console.Error.WriteLineAsync($"[DbLogBackgroundWriter] Failed to persist {batch.Count} system log entries: {ex.Message}");
            }
            catch
            {
                // Never crash
            }
        }
        finally
        {
            batch.Clear();
        }
    }
}
