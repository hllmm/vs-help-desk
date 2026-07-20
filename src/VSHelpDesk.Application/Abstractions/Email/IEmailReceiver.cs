namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>
/// Inbound mail (IMAP). ProcessIncomingEmails — Hafta 2 (UC-002, BR-001, BR-005).
/// </summary>
public interface IEmailReceiver
{
    Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default);

    Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken = default);
}

public sealed record IncomingEmail(
    string MessageId,
    string FromAddress,
    string FromDisplayName,
    string Subject,
    string Body,
    bool IsHtml,
    DateTime ReceivedAt,
    IReadOnlyList<IncomingEmailAttachment> Attachments);

public sealed record IncomingEmailAttachment(
    string FileName,
    string ContentType,
    long FileSize,
    Stream Content);
