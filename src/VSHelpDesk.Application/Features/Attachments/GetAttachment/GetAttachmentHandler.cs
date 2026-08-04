using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Application.Features.Attachments.GetAttachment;

public sealed class GetAttachmentHandler(
    ITicketAttachmentRepository attachmentRepository,
    IFileStorage fileStorage,
    IMessageProvider? messages = null)
{
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;

    public async Task<GetAttachmentResult> HandleAsync(
        GetAttachmentQuery query,
        CancellationToken cancellationToken)
    {
        var attachment = await attachmentRepository.GetByIdAsync(query.AttachmentId, cancellationToken);

        if (attachment is null)
        {
            throw new NotFoundException(_messages.Get(MessageKeys.Attachments.NotFound, query.AttachmentId));
        }

        try
        {
            var stream = await fileStorage.OpenReadAsync(attachment.StoredFileName, cancellationToken);
            return new GetAttachmentResult(
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.FileSize,
                stream);
        }
        catch (FileNotFoundException)
        {
            throw new NotFoundException(
                _messages.Get(MessageKeys.Attachments.StorageFileNotFound, query.AttachmentId));
        }
    }
}
