
namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>Outbound SMTP abstraction.</summary>
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
    IReadOnlyList<EmailAttachment>? Attachments = null,
    string? TextBody = null);

public sealed record EmailAttachment(
    string FileName,
    string ContentType,
    Stream Content);
