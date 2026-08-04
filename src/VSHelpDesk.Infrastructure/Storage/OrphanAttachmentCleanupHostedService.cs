using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;

namespace VSHelpDesk.Infrastructure.Storage;

/// <summary>
/// Background service executing periodic cleanup of orphan attachments.
/// Physical files are deleted only after a configurable grace period, preventing
/// cleanup from racing an upload whose database transaction is still in flight.
/// </summary>
public sealed class OrphanAttachmentCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<OrphanAttachmentCleanupHostedService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _period;
    private readonly TimeSpan _gracePeriod;

    public OrphanAttachmentCleanupHostedService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<OrphanAttachmentCleanupHostedService> logger,
        IOptions<FileStorageOptions>? options = null,
        TimeProvider? timeProvider = null,
        TimeSpan? period = null,
        TimeSpan? gracePeriod = null)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var configured = options?.Value ?? new FileStorageOptions();
        _period = period ?? TimeSpan.FromMinutes(configured.OrphanCleanupPeriodMinutes);
        _gracePeriod = gracePeriod ?? TimeSpan.FromMinutes(configured.OrphanGracePeriodMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OrphanAttachmentCleanupHostedService started period={Period} gracePeriod={GracePeriod}",
            _period,
            _gracePeriod);

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
            var storageInspector = scope.ServiceProvider.GetService<IFileStorageInspector>();
            var attachmentRepo = scope.ServiceProvider.GetRequiredService<ITicketAttachmentRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await DeleteAgedStorageOrphansAsync(
                fileStorage,
                storageInspector,
                attachmentRepo,
                cancellationToken);

            var orphanDbRecords = await attachmentRepo
                .GetOrphansQueryable()
                .ToListAsync(cancellationToken);

            if (orphanDbRecords.Count > 0)
            {
                foreach (var orphan in orphanDbRecords)
                {
                    _logger.LogWarning(
                        "Found orphan DB attachment record ID {Id}, stored file {StoredFileName}. Deleting file and record.",
                        orphan.Id,
                        orphan.StoredFileName);
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

    private async Task DeleteAgedStorageOrphansAsync(
        IFileStorage fileStorage,
        IFileStorageInspector? storageInspector,
        ITicketAttachmentRepository attachmentRepo,
        CancellationToken cancellationToken)
    {
        if (storageInspector is null)
        {
            _logger.LogWarning(
                "File storage does not implement {InspectorType}; physical orphan cleanup was skipped.",
                nameof(IFileStorageInspector));
            return;
        }

        var cutoff = _timeProvider.GetUtcNow() - _gracePeriod;
        var storedFiles = await storageInspector.ListStoredFileEntriesAsync(cancellationToken);
        foreach (var storedFile in storedFiles)
        {
            if (storedFile.LastModifiedAtUtc > cutoff)
            {
                continue;
            }

            var dbAttachment = await attachmentRepo.GetByStoredFileNameAsync(
                storedFile.StoredFileName,
                cancellationToken);
            if (dbAttachment is not null)
            {
                continue;
            }

            _logger.LogWarning(
                "Found aged physical file without DB attachment metadata: {StoredFileName}. Deleting file.",
                storedFile.StoredFileName);
            await fileStorage.DeleteAsync(storedFile.StoredFileName, cancellationToken);
        }
    }
}
