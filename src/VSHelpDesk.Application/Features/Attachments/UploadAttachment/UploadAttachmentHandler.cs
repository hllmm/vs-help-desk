using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Common.Models;

namespace VSHelpDesk.Application.Features.Attachments.UploadAttachment;

/// <summary>
/// Handles portal attachment upload commands by delegating validation, storage,
/// and metadata persistence to <see cref="ITicketAttachmentWriter.TryWriteAsync"/>.
/// </summary>
public sealed class UploadAttachmentHandler(
    ITicketAttachmentWriter attachmentWriter,
    ITicketAttachmentRepository attachmentRepository,
    IMessageProvider? messages = null)
{
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;

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
            var notFoundReason = _messages.Get(MessageKeys.Attachments.MessageNotFound, command.TicketMessageId);
            if (result.SkipReason == notFoundReason)
            {
                throw new NotFoundException(result.SkipReason);
            }

            return Result.Failure<UploadAttachmentResult>(result.SkipReason ?? _messages.Get(MessageKeys.Attachments.FailedToStoreFile));
        }

        var attachment = await attachmentRepository.GetByIdAsync(result.AttachmentId!.Value, cancellationToken);
        if (attachment is null)
        {
            throw new NotFoundException(_messages.Get(MessageKeys.Attachments.NotFound, result.AttachmentId!.Value));
        }

        return Result.Success(new UploadAttachmentResult(
            attachment.Id,
            attachment.TicketMessageId,
            attachment.FileName,
            attachment.ContentType,
            attachment.FileSize,
            attachment.CreatedAt,
            attachment.ScanVerdict.ToString(),
            attachment.ScanVerdict == Domain.Enums.ScanVerdict.Unscanned ? "Attachment has not been virus-scanned." : null));
    }
}
