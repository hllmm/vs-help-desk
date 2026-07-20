namespace VSHelpDesk.Application.Features.Attachments.UploadAttachment;

public sealed record UploadAttachmentCommand(
    Guid TicketMessageId,
    string FileName,
    string ContentType,
    long DeclaredFileSize,
    Stream Content);
