using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Application.Features.Attachments.GetAttachment;

public sealed class GetAttachmentHandler(
    ITicketAttachmentRepository attachmentRepository,
    IFileStorage fileStorage)
{
    public async Task<GetAttachmentResult> HandleAsync(
        GetAttachmentQuery query,
        CancellationToken cancellationToken)
    {
        var attachment = await attachmentRepository.GetByIdAsync(query.AttachmentId, cancellationToken);

        if (attachment is null)
        {
            throw new NotFoundException(ApplicationMessages.Attachments.NotFound(query.AttachmentId));
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
                ApplicationMessages.Attachments.StorageFileNotFound(query.AttachmentId));
        }
    }
}
