namespace VSHelpDesk.Application.Abstractions.Storage;

/// <summary>
/// Shared attachment persist path for portal upload and inbound mail (BR-012).
/// Policy failures and storage errors return <see cref="TicketAttachmentWriteResult.Skipped"/>;
/// callers decide whether to surface failures.
/// </summary>
public interface ITicketAttachmentWriter
{
    Task<TicketAttachmentWriteResult> TryWriteAsync(
        Guid ticketMessageId,
        string fileName,
        string contentType,
        Stream content,
        long declaredSize,
        CancellationToken cancellationToken = default);
}

public sealed record TicketAttachmentWriteResult(
    bool WasStored,
    Guid? AttachmentId,
    string? SkipReason)
{
    public static TicketAttachmentWriteResult Stored(Guid attachmentId) =>
        new(WasStored: true, AttachmentId: attachmentId, SkipReason: null);

    public static TicketAttachmentWriteResult Skipped(string reason) =>
        new(WasStored: false, AttachmentId: null, SkipReason: reason);
}
