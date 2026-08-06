namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>
/// Inbound mail (IMAP). ProcessIncomingEmails — Hafta 2 (UC-002, BR-001, BR-005).
/// </summary>
public enum EmailReceiptKind
{
    Fake = 1,
    Imap = 2
}

public sealed record EmailReceiptHandle(
    EmailReceiptKind Kind,
    string Value);

public interface IEmailReceiver
{
    Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(
        CancellationToken cancellationToken = default);

    Task MarkAsProcessedAsync(
        EmailReceiptHandle receiptHandle,
        CancellationToken cancellationToken = default);
}

public sealed record IncomingEmail(
    string? MessageId,
    EmailReceiptHandle ReceiptHandle,
    string? FromAddress,
    string? FromDisplayName,
    string? Subject,
    string? Body,
    bool IsHtml,
    DateTime ReceivedAt,
    IReadOnlyList<IncomingEmailAttachment> Attachments,
    EmailAuthenticationVerdict? AuthenticationVerdict = null,
    long? RawSize = null,
    int TotalAttachmentCount = 0,
    bool IsOversized = false);

public sealed record IncomingEmailAttachment(
    string FileName,
    string ContentType,
    long FileSize,
    byte[] Content);
