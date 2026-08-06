using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common.IO;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Attachments;

/// <summary>
/// Validates and persists a file attachment on a ticket message.
/// Mirrors <see cref="UploadAttachment.UploadAttachmentHandler"/> policy steps;
/// returns skip results instead of throwing for expected rejections.
/// </summary>
public sealed class TicketAttachmentWriter(
    ITicketRepository ticketRepository,
    ITicketAttachmentRepository attachmentRepository,
    IUnitOfWork unitOfWork,
    IFileStorage fileStorage,
    IAttachmentUploadPolicy uploadPolicy,
    TimeProvider timeProvider,
    ILogger<TicketAttachmentWriter> logger,
    IMessageProvider? messages = null,
    ITemporaryFileFactory? temporaryFileFactory = null) : ITicketAttachmentWriter
{
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;
    private readonly ITemporaryFileFactory _temporaryFileFactory = temporaryFileFactory ?? new DefaultTemporaryFileFactory();

    public async Task<TicketAttachmentWriteResult> TryWriteAsync(
        Guid ticketMessageId,
        string fileName,
        string contentType,
        Stream content,
        long declaredSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FileNameRequired));
        }

        if (declaredSize <= 0)
        {
            return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FileContentRequired));
        }

        if (declaredSize > uploadPolicy.MaxFileSizeBytes)
        {
            return TicketAttachmentWriteResult.Skipped(
                _messages.Get(MessageKeys.Attachments.MaxSizeBytesExceeded, uploadPolicy.MaxFileSizeBytes));
        }

        if (!uploadPolicy.IsContentTypeAllowed(contentType))
        {
            return TicketAttachmentWriteResult.Skipped(
                _messages.Get(MessageKeys.Attachments.ContentTypeNotAllowed, contentType));
        }

        var messageExists = await ticketRepository.MessageExistsAsync(ticketMessageId, cancellationToken);
        if (!messageExists)
        {
            return TicketAttachmentWriteResult.Skipped(
                _messages.Get(MessageKeys.Attachments.MessageNotFound, ticketMessageId));
        }

        var safeFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FileNameRequired));
        }

        if (!uploadPolicy.IsFileNameValid(safeFileName))
        {
            return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FileNameRequired));
        }

        if (!uploadPolicy.IsExtensionConsistentWithContentType(safeFileName, contentType))
        {
            return TicketAttachmentWriteResult.Skipped(
                _messages.Get(MessageKeys.Attachments.ContentTypeMismatch));
        }

        FileStream? ownedTemp = null;
        string? tmpPath = null;
        try
        {
            long max = uploadPolicy.MaxFileSizeBytes;
            int initialReadLimit = (int)Math.Min(4096, max + 1);
            var header = new byte[initialReadLimit];
            int headerRead;
            try
            {
                if (content.CanSeek)
                {
                    content.Position = 0;
                }

                headerRead = await content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to read attachment header for messageId={MessageId}",
                    ticketMessageId);
                return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
            }


            Stream scanStream;
            Stream contentToSave;

            if (!content.CanSeek)
            {
                FileStream tmp;
                string path;
                try
                {
                    var created = _temporaryFileFactory.CreateTempFile();
                    tmp = created.Stream;
                    path = created.Path;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to create temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                ownedTemp = tmp;
                tmpPath = path;

                try
                {
                    if (headerRead > 0)
                    {
                        await ownedTemp.WriteAsync(header.AsMemory(0, headerRead), cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to spool header to temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                long total = headerRead;
                if (total > max)
                {
                    return TicketAttachmentWriteResult.Skipped(
                        _messages.Get(MessageKeys.Attachments.MaxSizeBytesExceeded, uploadPolicy.MaxFileSizeBytes));
                }

                var buf = new byte[8192];
                try
                {
                    while (total <= max)
                    {
                        long remaining = (max + 1) - total;
                        if (remaining <= 0) break;
                        int toRead = (int)Math.Min(buf.Length, remaining);
                        int read = await content.ReadAsync(buf.AsMemory(0, toRead), cancellationToken);
                        if (read == 0) break;
                        total += read;
                        await ownedTemp.WriteAsync(buf.AsMemory(0, read), cancellationToken);
                        if (total > max) break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to spool attachment to temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                if (total > max)
                {
                    return TicketAttachmentWriteResult.Skipped(
                        _messages.Get(MessageKeys.Attachments.MaxSizeBytesExceeded, uploadPolicy.MaxFileSizeBytes));
                }

                try
                {
                    await ownedTemp.FlushAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to flush temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                ownedTemp.Position = 0;
                scanStream = ownedTemp;
                contentToSave = ownedTemp;
            }
            else
            {
                long length = -1;
                try
                {
                    length = content.Length;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Length not supported, treat as unknown and continue without length check
                }

                if (length >= 0 && length > max)
                {
                    return TicketAttachmentWriteResult.Skipped(
                        _messages.Get(MessageKeys.Attachments.MaxSizeBytesExceeded, uploadPolicy.MaxFileSizeBytes));
                }

                // Bounded temp file spool for seekable too (Length is fast-reject only).
                // Prevents lying Length or large seekable bypassing max+1 check via direct content reuse.
                FileStream tmp;
                string path;
                try
                {
                    var created = _temporaryFileFactory.CreateTempFile();
                    tmp = created.Stream;
                    path = created.Path;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to create temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                ownedTemp = tmp;
                tmpPath = path;

                try
                {
                    if (headerRead > 0)
                    {
                        await ownedTemp.WriteAsync(header.AsMemory(0, headerRead), cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to spool header to temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                long total = headerRead;
                if (total > max)
                {
                    return TicketAttachmentWriteResult.Skipped(
                        _messages.Get(MessageKeys.Attachments.MaxSizeBytesExceeded, uploadPolicy.MaxFileSizeBytes));
                }

                var buf = new byte[8192];
                try
                {
                    while (total <= max)
                    {
                        long remaining = (max + 1) - total;
                        if (remaining <= 0) break;
                        int toRead = (int)Math.Min(buf.Length, remaining);
                        int read = await content.ReadAsync(buf.AsMemory(0, toRead), cancellationToken);
                        if (read == 0) break;
                        total += read;
                        await ownedTemp.WriteAsync(buf.AsMemory(0, read), cancellationToken);
                        if (total > max) break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to spool attachment to temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                if (total > max)
                {
                    return TicketAttachmentWriteResult.Skipped(
                        _messages.Get(MessageKeys.Attachments.MaxSizeBytesExceeded, uploadPolicy.MaxFileSizeBytes));
                }

                try
                {
                    await ownedTemp.FlushAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to flush temp file for messageId={MessageId}",
                        ticketMessageId);
                    return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
                }

                ownedTemp.Position = 0;
                scanStream = ownedTemp;
                contentToSave = ownedTemp;
            }

            bool consistent;
            try
            {
                var headerSpan = header.AsSpan(0, Math.Max(headerRead, 0));
                if (scanStream.CanSeek)
                {
                    scanStream.Position = 0;
                }

                consistent = uploadPolicy.IsDeclaredTypeConsistentWithContent(safeFileName, contentType, scanStream, headerSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Declared type consistency check failed for messageId={MessageId}",
                    ticketMessageId);
                return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.ContentTypeMismatch));
            }
            finally
            {
                try
                {
                    if (scanStream.CanSeek)
                    {
                        scanStream.Position = 0;
                    }
                }
                catch
                {
                }

                try
                {
                    if (!ReferenceEquals(scanStream, contentToSave) && contentToSave.CanSeek)
                    {
                        contentToSave.Position = 0;
                    }
                    else if (ReferenceEquals(scanStream, contentToSave) && contentToSave.CanSeek)
                    {
                        // already reset via scanStream, ensure still 0
                    }
                }
                catch
                {
                }
            }

            if (!consistent)
            {
                return TicketAttachmentWriteResult.Skipped(
                    _messages.Get(MessageKeys.Attachments.ContentTypeMismatch));
            }

            StoredFile stored;
            try
            {
                if (contentToSave.CanSeek)
                {
                    contentToSave.Position = 0;
                }

                stored = await fileStorage.SaveAsync(
                    contentToSave,
                    safeFileName,
                    contentType.Trim(),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to write attachment to storage for messageId={MessageId}",
                    ticketMessageId);
                return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToStoreFile));
            }

            if (stored.FileSize > uploadPolicy.MaxFileSizeBytes)
            {
                await TryDeleteAsync(stored.StoredFileName, cancellationToken);
                return TicketAttachmentWriteResult.Skipped(
                    _messages.Get(MessageKeys.Attachments.MaxSizeBytesExceeded, uploadPolicy.MaxFileSizeBytes));
            }

            if (stored.FileSize <= 0)
            {
                await TryDeleteAsync(stored.StoredFileName, cancellationToken);
                return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FileContentRequired));
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var attachment = new TicketAttachment(
                ticketMessageId,
                safeFileName,
                stored.StoredFileName,
                stored.FilePath,
                stored.ContentType,
                stored.FileSize,
                now);

            try
            {
                await attachmentRepository.AddAsync(attachment, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to persist attachment metadata; rolling back storage file={StoredFileName}",
                    stored.StoredFileName);
                await TryDeleteAsync(stored.StoredFileName, cancellationToken);
                return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToPersistMetadata));
            }

            logger.LogInformation(
                "Attachment stored attachmentId={AttachmentId} messageId={MessageId} storedFileName={StoredFileName} size={FileSize}",
                attachment.Id,
                attachment.TicketMessageId,
                attachment.StoredFileName,
                attachment.FileSize);

            return TicketAttachmentWriteResult.Stored(attachment.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            if (ownedTemp is not null)
            {
                try
                {
                    await ownedTemp.DisposeAsync();
                }
                catch
                {
                }
            }

            if (tmpPath is not null && File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch
                {
                }
            }
        }
    }

    private async Task TryDeleteAsync(string storedFileName, CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteAsync(storedFileName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to delete orphaned storage file storedFileName={StoredFileName}",
                storedFileName);
        }
    }
}
