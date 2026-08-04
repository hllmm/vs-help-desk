using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Logging;

public class DbLogBackgroundWriter : BackgroundService
{
    private readonly ChannelReader<SystemLog> _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private const int BatchSize = 100;

    public DbLogBackgroundWriter(ChannelReader<SystemLog> reader, IServiceScopeFactory scopeFactory)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SystemLog>(BatchSize);

        try
        {
            await foreach (var log in _reader.ReadAllAsync(stoppingToken))
            {
                batch.Add(log);

                if (batch.Count >= BatchSize || _reader.Count == 0)
                {
                    await WriteBatchAsync(batch, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore when stopping
        }
        catch
        {
            // Fallback: never crash
        }
    }

    private async Task WriteBatchAsync(List<SystemLog> batch, CancellationToken stoppingToken)
    {
        if (batch.Count == 0)
            return;

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
        catch
        {
            // Never crash
        }
        finally
        {
            batch.Clear();
        }
    }
}
