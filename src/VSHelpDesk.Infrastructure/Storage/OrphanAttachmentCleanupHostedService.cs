using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;

namespace VSHelpDesk.Infrastructure.Storage;

/// <summary>
/// BackgroundService executing periodic cleanup of orphan attachments.
/// Scans IFileStorage and ITicketAttachmentRepository to remove physical files and DB metadata
/// when attachment records or files are missing.
/// </summary>
public sealed class OrphanAttachmentCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<OrphanAttachmentCleanupHostedService> _logger;
    private readonly TimeSpan _period;

    public OrphanAttachmentCleanupHostedService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<OrphanAttachmentCleanupHostedService> logger,
        TimeSpan? period = null)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _period = period ?? TimeSpan.FromMinutes(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrphanAttachmentCleanupHostedService started with period {Period}", _period);

        using var timer = new PeriodicTimer(_period);
        try
        {
            await CleanupOrphanAttachmentsAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CleanupOrphanAttachmentsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("OrphanAttachmentCleanupHostedService stopping.");
        }
    }

    public async Task CleanupOrphanAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting orphan attachment cleanup pass.");
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var attachmentRepo = scope.ServiceProvider.GetRequiredService<ITicketAttachmentRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Pass 1: Clean physical files that have no matching DB metadata
            var storedFiles = await fileStorage.ListStoredFilesAsync(cancellationToken);
            foreach (var storedFileName in storedFiles)
            {
                var dbAttachment = await attachmentRepo.GetByStoredFileNameAsync(storedFileName, cancellationToken);
                if (dbAttachment is null)
                {
                    _logger.LogWarning("Found physical file without DB attachment metadata: {StoredFileName}. Deleting file.", storedFileName);
                    await fileStorage.DeleteAsync(storedFileName, cancellationToken);
                }
            }

            // Pass 2: Clean DB records for attachments whose parent ticket message no longer exists
            var orphanDbRecords = await attachmentRepo.GetOrphansQueryable().ToListAsync(cancellationToken);
            if (orphanDbRecords.Count > 0)
            {
                foreach (var orphan in orphanDbRecords)
                {
                    _logger.LogWarning("Found orphan DB attachment record ID {Id}, stored file {StoredFileName}. Deleting file and record.", orphan.Id, orphan.StoredFileName);
                    await fileStorage.DeleteAsync(orphan.StoredFileName, cancellationToken);
                    attachmentRepo.Remove(orphan);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Orphan attachment cleanup pass completed successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during orphan attachment cleanup pass.");
        }
    }
}
