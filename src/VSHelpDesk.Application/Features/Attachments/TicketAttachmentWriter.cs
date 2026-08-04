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
    IMessageProvider? messages = null) : ITicketAttachmentWriter
{
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;

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

        // Sniff leading bytes before persisting (do not trust declared Content-Type alone).
        Stream contentToSave = content;
        PrefixStream? prefixStream = null;
        var header = new byte[16];
        int read;
        try
        {
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            read = await content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            if (content.CanSeek)
            {
                content.Position = 0;
            }
            else if (read > 0)
            {
                prefixStream = new PrefixStream(header.AsMemory(0, read), content);
                contentToSave = prefixStream;
            }
        }
        catch (Exception ex)
        {
            if (prefixStream is not null)
            {
                await prefixStream.DisposeAsync();
            }

            logger.LogError(
                ex,
                "Failed to read attachment header for messageId={MessageId}",
                ticketMessageId);
            return TicketAttachmentWriteResult.Skipped(_messages.Get(MessageKeys.Attachments.FailedToReadFile));
        }

        try
        {
            if (!uploadPolicy.IsDeclaredTypeConsistentWithContent(
                    contentType,
                    header.AsSpan(0, Math.Max(read, 0))))
            {
                return TicketAttachmentWriteResult.Skipped(
                    _messages.Get(MessageKeys.Attachments.ContentTypeMismatch));
            }

            StoredFile stored;
            try
            {
                stored = await fileStorage.SaveAsync(
                    contentToSave,
                    safeFileName,
                    contentType.Trim(),
                    cancellationToken);
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
        finally
        {
            if (prefixStream is not null)
            {
                await prefixStream.DisposeAsync();
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
