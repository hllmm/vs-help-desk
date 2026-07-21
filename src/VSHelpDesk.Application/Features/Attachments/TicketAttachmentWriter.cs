using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Attachments;

/// <summary>
/// Validates and persists a file attachment on a ticket message.
/// Mirrors <see cref="UploadAttachment.UploadAttachmentHandler"/> policy steps;
/// returns skip results instead of throwing for expected rejections.
/// </summary>
public sealed class TicketAttachmentWriter(
    IApplicationDbContext applicationDbContext,
    IFileStorage fileStorage,
    IAttachmentUploadPolicy uploadPolicy,
    TimeProvider timeProvider,
    ILogger<TicketAttachmentWriter> logger) : ITicketAttachmentWriter
{
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
            return TicketAttachmentWriteResult.Skipped("File name is required.");
        }

        if (declaredSize <= 0)
        {
            return TicketAttachmentWriteResult.Skipped("File content is required.");
        }

        if (declaredSize > uploadPolicy.MaxFileSizeBytes)
        {
            return TicketAttachmentWriteResult.Skipped(
                $"File exceeds the maximum allowed size of {uploadPolicy.MaxFileSizeBytes} bytes.");
        }

        if (!uploadPolicy.IsContentTypeAllowed(contentType))
        {
            return TicketAttachmentWriteResult.Skipped(
                $"Content type '{contentType}' is not allowed.");
        }

        var messageExists = applicationDbContext.TicketMessages
            .Any(message => message.Id == ticketMessageId);
        if (!messageExists)
        {
            return TicketAttachmentWriteResult.Skipped(
                $"Ticket message '{ticketMessageId}' was not found.");
        }

        var safeFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return TicketAttachmentWriteResult.Skipped("File name is required.");
        }

        // Sniff leading bytes before persisting (do not trust declared Content-Type alone).
        Stream contentToSave = content;
        MemoryStream? ownedStream = null;
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
                var remainder = new MemoryStream();
                await content.CopyToAsync(remainder, cancellationToken);
                remainder.Position = 0;
                ownedStream = new MemoryStream();
                await ownedStream.WriteAsync(header.AsMemory(0, read), cancellationToken);
                await remainder.CopyToAsync(ownedStream, cancellationToken);
                ownedStream.Position = 0;
                contentToSave = ownedStream;
            }
        }
        catch (Exception ex)
        {
            if (ownedStream is not null)
            {
                await ownedStream.DisposeAsync();
            }

            logger.LogError(
                ex,
                "Failed to read attachment header for messageId={MessageId}",
                ticketMessageId);
            return TicketAttachmentWriteResult.Skipped("Failed to read the uploaded file.");
        }

        try
        {
            if (!uploadPolicy.IsDeclaredTypeConsistentWithContent(
                    contentType,
                    header.AsSpan(0, Math.Max(read, 0))))
            {
                return TicketAttachmentWriteResult.Skipped(
                    "File content does not match the declared content type.");
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
                return TicketAttachmentWriteResult.Skipped("Failed to store the uploaded file.");
            }

            if (stored.FileSize > uploadPolicy.MaxFileSizeBytes)
            {
                await TryDeleteAsync(stored.StoredFileName, cancellationToken);
                return TicketAttachmentWriteResult.Skipped(
                    $"File exceeds the maximum allowed size of {uploadPolicy.MaxFileSizeBytes} bytes.");
            }

            if (stored.FileSize <= 0)
            {
                await TryDeleteAsync(stored.StoredFileName, cancellationToken);
                return TicketAttachmentWriteResult.Skipped("File content is required.");
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
                applicationDbContext.Add(attachment);
                await applicationDbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to persist attachment metadata; rolling back storage file={StoredFileName}",
                    stored.StoredFileName);
                await TryDeleteAsync(stored.StoredFileName, cancellationToken);
                return TicketAttachmentWriteResult.Skipped("Failed to persist attachment metadata.");
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
            if (ownedStream is not null)
            {
                await ownedStream.DisposeAsync();
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
