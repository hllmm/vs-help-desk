namespace VSHelpDesk.Application.Features.Tickets.GetTicketDetails;

public sealed record TicketMessagePageDto(
    IReadOnlyList<TicketMessageDto> Messages,
    IReadOnlyList<TicketAttachmentMetaDto> Attachments,
    string? NextCursor,
    bool HasMore);
