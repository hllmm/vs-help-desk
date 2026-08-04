using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Models;

namespace VSHelpDesk.Application.Features.Attachments.UploadAttachment;

/// <summary>
/// Handles portal attachment upload commands by delegating validation, storage,
/// and metadata persistence to <see cref="ITicketAttachmentWriter.TryWriteAsync"/>.
/// </summary>
public sealed class UploadAttachmentHandler(
    ITicketAttachmentWriter attachmentWriter,
    ITicketAttachmentRepository attachmentRepository)
{
    public async Task<Result<UploadAttachmentResult>> HandleAsync(
        UploadAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var result = await attachmentWriter.TryWriteAsync(
            command.TicketMessageId,
            command.FileName,
            command.ContentType,
            command.Content,
            command.DeclaredFileSize,
            cancellationToken);

        if (!result.WasStored)
        {
            var notFoundReason = ApplicationMessages.Attachments.MessageNotFound(command.TicketMessageId);
            if (result.SkipReason == notFoundReason)
            {
                throw new NotFoundException(result.SkipReason);
            }

            return Result.Failure<UploadAttachmentResult>(result.SkipReason ?? ApplicationMessages.Attachments.FailedToStoreFile);
        }

        var attachment = await attachmentRepository.GetByIdAsync(result.AttachmentId!.Value, cancellationToken);
        if (attachment is null)
        {
            throw new NotFoundException(ApplicationMessages.Attachments.NotFound(result.AttachmentId!.Value));
        }

        return Result.Success(new UploadAttachmentResult(
            attachment.Id,
            attachment.TicketMessageId,
            attachment.FileName,
            attachment.ContentType,
            attachment.FileSize,
            attachment.CreatedAt));
    }
}
