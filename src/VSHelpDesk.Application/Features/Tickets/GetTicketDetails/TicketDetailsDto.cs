namespace VSHelpDesk.Application.Features.Tickets.GetTicketDetails;

public sealed record TicketDetailsDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string CustomerName,
    string CustomerEmail,
    string Status,
    Guid? AssignedUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime LastActivityAt,
    DateTime? WaitingCustomerSince,
    DateTime? ResolvedAt,
    Guid? ClosedByUserId,
    IReadOnlyList<TicketMessageDto> Messages,
    IReadOnlyList<TicketAttachmentMetaDto> Attachments,
    string? NextMessageCursor,
    bool HasMoreMessages);

public sealed record TicketMessageDto(
    Guid Id,
    string SenderType,
    Guid? UserId,
    string Content,
    bool IsHtml,
    DateTime CreatedAt);

/// <summary>Attachment metadata only (BR-012); binary content via GET /api/attachments/{id}.</summary>
public sealed record TicketAttachmentMetaDto(
    Guid Id,
    Guid TicketMessageId,
    string FileName,
    string ContentType,
    long FileSize,
    DateTime CreatedAt);
