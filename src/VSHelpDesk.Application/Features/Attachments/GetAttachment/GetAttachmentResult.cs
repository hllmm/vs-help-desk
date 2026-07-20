namespace VSHelpDesk.Application.Features.Attachments.GetAttachment;

public sealed record GetAttachmentResult(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    Stream Content);
