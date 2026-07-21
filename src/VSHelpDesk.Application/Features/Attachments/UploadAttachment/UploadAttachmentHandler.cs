using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Attachments.UploadAttachment;

public sealed class UploadAttachmentHandler(
    IApplicationDbContext applicationDbContext,
    IFileStorage fileStorage,
    IAttachmentUploadPolicy uploadPolicy,
    TimeProvider timeProvider,
    ILogger<UploadAttachmentHandler> logger)
{
    public async Task<Result<UploadAttachmentResult>> HandleAsync(
        UploadAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.FileName))
        {
            return Result.Failure<UploadAttachmentResult>("File name is required.");
        }

        if (command.DeclaredFileSize <= 0)
        {
            return Result.Failure<UploadAttachmentResult>("File content is required.");
        }

        if (command.DeclaredFileSize > uploadPolicy.MaxFileSizeBytes)
        {
            return Result.Failure<UploadAttachmentResult>(
                $"File exceeds the maximum allowed size of {uploadPolicy.MaxFileSizeBytes} bytes.");
        }

        if (!uploadPolicy.IsContentTypeAllowed(command.ContentType))
        {
            return Result.Failure<UploadAttachmentResult>(
                $"Content type '{command.ContentType}' is not allowed.");
        }

        var messageExists = applicationDbContext.TicketMessages
            .Any(message => message.Id == command.TicketMessageId);
        if (!messageExists)
        {
            throw new NotFoundException($"Ticket message '{command.TicketMessageId}' was not found.");
        }

        var safeFileName = Path.GetFileName(command.FileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return Result.Failure<UploadAttachmentResult>("File name is required.");
        }

        // Sniff leading bytes before persisting (do not trust client Content-Type alone).
        var header = new byte[16];
        int read;
        try
        {
            if (command.Content.CanSeek)
            {
                command.Content.Position = 0;
            }

            read = await command.Content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            if (command.Content.CanSeek)
            {
                command.Content.Position = 0;
            }
            else if (read > 0)
            {
                // Non-seekable: rebuild a stream that re-plays the header then the remainder.
                var remainder = new MemoryStream();
                await command.Content.CopyToAsync(remainder, cancellationToken);
                remainder.Position = 0;
                var combined = new MemoryStream();
                await combined.WriteAsync(header.AsMemory(0, read), cancellationToken);
                await remainder.CopyToAsync(combined, cancellationToken);
                combined.Position = 0;
                command = command with { Content = combined };
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to read attachment header for messageId={MessageId}",
                command.TicketMessageId);
            return Result.Failure<UploadAttachmentResult>("Failed to read the uploaded file.");
        }

        if (!uploadPolicy.IsDeclaredTypeConsistentWithContent(
                command.ContentType,
                header.AsSpan(0, Math.Max(read, 0))))
        {
            return Result.Failure<UploadAttachmentResult>(
                "File content does not match the declared content type.");
        }

        StoredFile stored;
        try
        {
            stored = await fileStorage.SaveAsync(
                command.Content,
                safeFileName,
                command.ContentType.Trim(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to write attachment to storage for messageId={MessageId}",
                command.TicketMessageId);
            return Result.Failure<UploadAttachmentResult>("Failed to store the uploaded file.");
        }

        if (stored.FileSize > uploadPolicy.MaxFileSizeBytes)
        {
            await TryDeleteAsync(stored.StoredFileName, cancellationToken);
            return Result.Failure<UploadAttachmentResult>(
                $"File exceeds the maximum allowed size of {uploadPolicy.MaxFileSizeBytes} bytes.");
        }

        if (stored.FileSize <= 0)
        {
            await TryDeleteAsync(stored.StoredFileName, cancellationToken);
            return Result.Failure<UploadAttachmentResult>("File content is required.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attachment = new TicketAttachment(
            command.TicketMessageId,
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
            throw;
        }

        logger.LogInformation(
            "Attachment uploaded attachmentId={AttachmentId} messageId={MessageId} storedFileName={StoredFileName} size={FileSize}",
            attachment.Id,
            attachment.TicketMessageId,
            attachment.StoredFileName,
            attachment.FileSize);

        return Result.Success(new UploadAttachmentResult(
            attachment.Id,
            attachment.TicketMessageId,
            attachment.FileName,
            attachment.ContentType,
            attachment.FileSize,
            attachment.CreatedAt));
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
