namespace VSHelpDesk.Application.Features.Attachments.UploadAttachment;

public sealed record UploadAttachmentResult(
    Guid Id,
    Guid TicketMessageId,
    string FileName,
    string ContentType,
    long FileSize,
    DateTime CreatedAt,
    string ScanVerdict = "Unscanned",
    string? ScanWarning = null);
