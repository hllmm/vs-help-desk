namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>
/// Outbound mail (SMTP). Used for ack + support replies (BR-002, BR-006, BR-022) — Hafta 2/3.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    string ToAddress,
    string ToDisplayName,
    string Subject,
    string Body,
    bool IsHtml = false,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record EmailAttachment(
    string FileName,
    string ContentType,
    Stream Content);
